using System.Diagnostics;
using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.Service;

/// <summary>Nome da memória compartilhada do batimento (o simulado usa um nome local, sem precisar de admin).</summary>
public sealed record HeartbeatOptions(string Name);

/// <summary>Nome do pipe da interface (o simulado usa outro nome).</summary>
public sealed record IpcOptions(string PipeName);

/// <summary>
/// Laço de 1 s do serviço: roda o <see cref="FanControlEngine"/>, publica o batimento e registra mudanças de estado.
/// Começa e termina SEMPRE no Auto do EC (docs/arquitetura.md §6.3).
/// </summary>
public sealed partial class FanControlWorker : BackgroundService
{
    private readonly FanControlEngine _engine;
    private readonly IGpuSensor _gpu;
    private readonly GpuTemperatureFeed _gpuFeed = new();
    private readonly HeartbeatOptions _heartbeat;
    private readonly IpcServer _ipc;
    private readonly ProfileStore? _store;
    private readonly ControlStateStore? _control;
    private readonly ILogger<FanControlWorker> _log;
    private readonly Lock _gate = new();
    private readonly Func<bool?> _power = KillerSense.Firmware.PowerSource.IsOnAc; // leitura do Windows, também no simulado
    private readonly ChargeLimitMonitor _chargeLimit;
    private bool _suspended;
    private (EngineState State, FanMode Cpu, FanMode Gpu)? _lastLogged;
    private bool? _lastLoggedChargeLimit;

    /// <param name="store">Onde o perfil é salvo; null = não persistir (ex.: teste com --cpu-entry).</param>
    /// <param name="control">Onde fica a escolha "Encerrar"; null = não persistir.</param>
    public FanControlWorker(FanControlEngine engine, ChargeLimitMonitor chargeLimit, IGpuSensor gpu, HeartbeatOptions heartbeat, IpcOptions ipc, ProfileStore? store, ControlStateStore? control, ILogger<FanControlWorker> log)
    {
        _chargeLimit = chargeLimit;
        _control = control;
        _engine = engine;
        _gpu = gpu;
        _heartbeat = heartbeat;
        _store = store;
        _log = log;
        _ipc = new IpcServer(ipc.PipeName, HandleRequestAsync);
    }

    /// <summary>Pedidos da interface: "Voltar ao Auto" (pausa), "Retomar", perfil de fans e modo de operação.</summary>
    private Task<IpcMessage> HandleRequestAsync(IpcRequest request)
    {
        var type = request.Type;
        try
        {
            lock (_gate)
            {
                switch (type)
                {
                    case IpcRequestType.RestoreAuto:
                        _engine.Pause("Controle pausado pelo usuário (Voltar ao Auto).");
                        break;
                    case IpcRequestType.Resume:
                        _engine.RestoreAuto();
                        _engine.Resume();
                        break;
                    case IpcRequestType.GetProfile:
                        return Task.FromResult(IpcMessage.ForProfile(FanProfileDto.From(_engine.Profile)));
                    case IpcRequestType.Disable:
                        _engine.Disable("killer-sense encerrado pelo usuário: fans no Auto do EC, PredatorSense no controle.");
                        _control?.SetDisabled(true);
                        break;
                    case IpcRequestType.Enable:
                        _control?.SetDisabled(false);
                        _engine.RestoreAuto();
                        _engine.Resume();
                        break;
                    case IpcRequestType.SetOperatingMode:
                        if (request.Mode is not { } mode || !Enum.IsDefined(mode))
                        {
                            return Task.FromResult(IpcMessage.Ack(false, "Modo ausente ou inválido."));
                        }

                        _engine.SetOperatingMode(mode, _power());
                        LogModeSet(_log, mode);
                        break;
                    case IpcRequestType.SetChargeLimit:
                        if (request.Enabled is not { } enabled)
                        {
                            return Task.FromResult(IpcMessage.Ack(false, "Valor do limite ausente."));
                        }

                        if (_engine.IsDisabled)
                        {
                            throw new InvalidOperationException("O killer-sense está desativado; ative-o para mudar o limite de carga.");
                        }

                        _chargeLimit.Set(enabled);
                        LogChargeLimitSet(_log, enabled);
                        break;
                    case IpcRequestType.SetProfile:
                        string? error = "Perfil ausente.";
                        if (request.Profile?.TryToProfile(out error) is not { } profile)
                        {
                            error ??= "Perfil inválido.";
                            LogProfileRejected(_log, error);
                            return Task.FromResult(IpcMessage.Ack(false, error));
                        }

                        _store?.Save(profile); // grava antes de aplicar: o que roda é o que está salvo
                        _engine.SetProfile(profile);
                        break;
                    default:
                        return Task.FromResult(IpcMessage.Ack(false, "Comando não suportado."));
                }
            }

            LogCommand(_log, type);
            return Task.FromResult(IpcMessage.Ack(true));
        }
        catch (FirmwareException ex)
        {
            LogCommandFailed(_log, ex, type);
            return Task.FromResult(IpcMessage.Ack(false, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            LogCommandRefused(_log, type, ex.Message);
            return Task.FromResult(IpcMessage.Ack(false, ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCommandFailed(_log, ex, type);
            return Task.FromResult(IpcMessage.Ack(false, $"Não foi possível salvar o perfil: {ex.Message}"));
        }
    }

    /// <summary>Suspensão do Windows: Auto antes de dormir e controle pausado.</summary>
    public void Suspend() => Guarded("suspensão", () =>
    {
        _suspended = true;
        _engine.Pause("Sistema em suspensão.", readMode: false);
    });

    /// <summary>Retomada: recomeça do Auto e libera o controle.</summary>
    public void Resume() => Guarded("retomada", () =>
    {
        _suspended = false;
        _engine.RestoreAuto();
        _engine.Resume();
    });

    /// <summary>Melhor esforço para devolver o Auto (desligamento, exceção não tratada).</summary>
    public void RestoreAutoNow(string reason) => Guarded(reason, _engine.RestoreAuto);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var heartbeat = Heartbeat.CreateOrOpen(_heartbeat.Name);
        RestoreAutoNow("início do serviço");
        if (_control?.IsDisabled == true)
        {
            Guarded("início desativado", () => _engine.Disable("killer-sense encerrado pelo usuário: fans no Auto do EC, PredatorSense no controle."));
        }
        LogStarted(_log);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var sw = Stopwatch.StartNew();
        var ipcLoop = Task.Run(() => _ipc.RunAsync(stoppingToken), stoppingToken);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var dt = sw.Elapsed.TotalSeconds;
                sw.Restart();
                var gpuTemp = _gpuFeed.Update(_gpu.Read()); // não acorda a GPU desligada
                EngineSnapshot snap;
                bool? chargeLimit;
                lock (_gate)
                {
                    snap = _engine.Tick(dt, gpuTemp);
                    var batteryAllowed = !_engine.IsDisabled && !_suspended; // desativado/suspenso: nenhuma chamada
                    _chargeLimit.Tick(dt, batteryAllowed);
                    chargeLimit = batteryAllowed ? _chargeLimit.IsOn : null;
                }

                heartbeat.Beat(DateTime.UtcNow, snap.ManualActive);
                _ipc.Publish(ServiceState.From(snap, DateTime.UtcNow, _power(), chargeLimit));
                if (chargeLimit != _lastLoggedChargeLimit)
                {
                    LogChargeLimitState(_log, chargeLimit);
                    _lastLoggedChargeLimit = chargeLimit;
                }
                if (ipcLoop.IsFaulted)
                {
                    LogIpcFailed(_log, ipcLoop.Exception!);
                    ipcLoop = Task.Run(() => _ipc.RunAsync(stoppingToken), stoppingToken);
                }

                var key = (snap.State, snap.Cpu.Mode, snap.Gpu.Mode);
                if (key != _lastLogged)
                {
                    LogState(_log, snap.State, snap.CpuTemperature, snap.GpuTemperature, snap.Cpu.Mode, snap.Cpu.Percent, snap.Gpu.Mode, snap.Gpu.Percent, snap.Message);
                    _lastLogged = key;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // parada normal
        }
        finally
        {
            RestoreAutoNow("parada do serviço");
            heartbeat.Beat(DateTime.UtcNow, manualActive: false);
            LogStopped(_log);
        }
    }

    private void Guarded(string reason, Action action)
    {
        try
        {
            lock (_gate)
            {
                action();
            }

            LogAuto(_log, reason);
        }
        catch (FirmwareException ex)
        {
            LogAutoFailed(_log, ex, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Comando da interface executado: {Command}.")]
    private static partial void LogCommand(ILogger logger, IpcRequestType command);

    [LoggerMessage(Level = LogLevel.Information, Message = "Modo de operação trocado pela interface: {Mode}.")]
    private static partial void LogModeSet(ILogger logger, OperatingMode mode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Limite de carga trocado pela interface: {Enabled}.")]
    private static partial void LogChargeLimitSet(ILogger logger, bool enabled);

    [LoggerMessage(Level = LogLevel.Information, Message = "Limite de carga da bateria: {State} (null = desconhecido ou sem leitura).")]
    private static partial void LogChargeLimitState(ILogger logger, bool? state);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comando {Command} recusado: {Reason}")]
    private static partial void LogCommandRefused(ILogger logger, IpcRequestType command, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Perfil recusado: {Reason}")]
    private static partial void LogProfileRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Critical, Message = "FALHA ao executar o comando da interface {Command}.")]
    private static partial void LogCommandFailed(ILogger logger, Exception ex, IpcRequestType command);

    [LoggerMessage(Level = LogLevel.Error, Message = "Servidor do pipe da interface falhou; reiniciando.")]
    private static partial void LogIpcFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "killer-sense iniciado; fans no Auto do EC.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "killer-sense parado; fans no Auto do EC.")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto do EC aplicado ({Reason}).")]
    private static partial void LogAuto(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Critical, Message = "FALHA ao aplicar o Auto do EC ({Reason}).")]
    private static partial void LogAutoFailed(ILogger logger, Exception ex, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Estado {State}: CPU {CpuTemp} °C, GPU {GpuTemp} °C | fan CPU {CpuMode} {CpuPct}% | fan GPU {GpuMode} {GpuPct}% | {Message}")]
    private static partial void LogState(ILogger logger, EngineState state, double? cpuTemp, double? gpuTemp, FanMode cpuMode, int? cpuPct, FanMode gpuMode, int? gpuPct, string? message);
}
