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
    private readonly GpuOffsetController _gpuOffsets;
    private readonly GpuOffsetStore? _gpuOffsetStore;
    private readonly LightingController _lighting;
    private readonly LightingStore? _lightingStore;
    private readonly LightingRecheck _lightingRecheck = new();
    private bool _suspended;
    private (EngineState State, FanMode Cpu, FanMode Gpu)? _lastLogged;
    private bool? _lastLoggedChargeLimit;

    /// <param name="store">Onde o perfil é salvo; null = não persistir (ex.: teste com --cpu-entry).</param>
    /// <param name="control">Onde fica a escolha "Encerrar"; null = não persistir.</param>
    /// <param name="gpuOffsets">Quem lê e grava o offset de clock da GPU (NVAPI ou simulado).</param>
    /// <param name="gpuOffsetStore">Onde fica a configuração do offset; null = só o padrão (desligado), sem salvar.</param>
    /// <param name="lighting">Luz do teclado (já com a configuração salva).</param>
    /// <param name="lightingStore">Onde fica a configuração da luz; null = não salvar.</param>
    public FanControlWorker(FanControlEngine engine, ChargeLimitMonitor chargeLimit, IGpuSensor gpu, IGpuClockOffsets gpuOffsets, HeartbeatOptions heartbeat, IpcOptions ipc, ProfileStore? store, ControlStateStore? control, GpuOffsetStore? gpuOffsetStore, LightingController lighting, LightingStore? lightingStore, ILogger<FanControlWorker> log)
    {
        ArgumentNullException.ThrowIfNull(gpuOffsets);
        ArgumentNullException.ThrowIfNull(lighting);
        _lighting = lighting;
        _lightingStore = lightingStore;
        _gpuOffsetStore = gpuOffsetStore;
        var offsetSettings = GpuOffsetSettings.Default;
        if (gpuOffsetStore is not null)
        {
            offsetSettings = gpuOffsetStore.Load(out var problem);
            if (problem is not null)
            {
                LogGpuOffsetConfigIgnored(log, problem);
            }
        }

        _gpuOffsets = new GpuOffsetController(gpuOffsets, offsetSettings, message => LogGpuOffset(log, message));
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
        var reapplyLighting = false;
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
                        reapplyLighting = true;
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
                    case IpcRequestType.GetGpuOffsets:
                        return Task.FromResult(IpcMessage.ForGpuOffsets(GpuOffsetSettingsDto.From(_gpuOffsets.Settings)));
                    case IpcRequestType.SetGpuOffsets:
                        string? offsetError = "Configuração ausente.";
                        if (request.GpuOffsets?.TryToSettings(out offsetError) is not { } offsets)
                        {
                            offsetError ??= "Configuração inválida.";
                            LogGpuOffsetRejected(_log, offsetError);
                            return Task.FromResult(IpcMessage.Ack(false, offsetError));
                        }

                        _gpuOffsetStore?.Save(offsets); // grava antes de aplicar
                        _gpuOffsets.SetSettings(offsets);
                        var turbo = offsets.For(OperatingMode.Turbo);
                        LogGpuOffsetSaved(_log, offsets.Enabled, turbo);
                        break;
                    case IpcRequestType.GetLighting:
                        return Task.FromResult(GetLighting());
                    case IpcRequestType.SetLighting:
                        string? lightingError = "Configuração da luz ausente.";
                        if (request.Lighting?.TryToSettings(out lightingError) is not { } lighting)
                        {
                            lightingError ??= "Configuração da luz inválida.";
                            LogLightingRejected(_log, lightingError);
                            return Task.FromResult(IpcMessage.Ack(false, lightingError));
                        }

                        _lightingStore?.Save(lighting); // grava antes de aplicar
                        LogLightingSaved(_log, lighting.Enabled, lighting.Lighting);
                        if (lighting.Enabled && !LightingAllowed)
                        {
                            _lighting.SetSettings(lighting, allowed: false);
                            return Task.FromResult(IpcMessage.Ack(true, "Configuração salva. O killer-sense está desativado; a luz será gravada quando ele voltar."));
                        }

                        _lighting.SetSettings(lighting, allowed: true);
                        break;
                    default:
                        return Task.FromResult(IpcMessage.Ack(false, "Comando não suportado."));
                }
            }

            LogCommand(_log, type);
            if (reapplyLighting)
            {
                ApplyLightingIfEnabled("killer-sense reativado");
            }

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
            return Task.FromResult(IpcMessage.Ack(false, $"Não foi possível salvar a configuração: {ex.Message}"));
        }
    }

    /// <summary>Luz só com o killer-sense ativo e fora da suspensão (chamar sob <see cref="_gate"/>).</summary>
    private bool LightingAllowed => !_engine.IsDisabled && !_suspended;

    /// <summary>Configuração da luz e, com o controle ligado, a leitura do teclado (chamar sob <see cref="_gate"/>).</summary>
    private IpcMessage GetLighting()
    {
        var settings = LightingSettingsDto.From(_lighting.Settings);
        try
        {
            var current = _lighting.ReadCurrent(LightingAllowed);
            return IpcMessage.ForLighting(settings, current is null ? null : KeyboardLightingDto.From(current));
        }
        catch (FirmwareException ex)
        {
            LogLightingReadFailed(_log, ex);
            return IpcMessage.ForLighting(settings, null, $"Não foi possível ler a luz do teclado: {ex.Message}");
        }
    }

    /// <summary>Grava a luz salva, se o controle da luz estiver ligado (início do serviço e ao reativar).</summary>
    private void ApplyLightingIfEnabled(string reason)
    {
        try
        {
            lock (_gate)
            {
                if (_lighting.ApplyIfEnabled(LightingAllowed))
                {
                    LogLightingApplied(_log, reason, _lighting.Settings.Lighting);
                }
            }
        }
        catch (FirmwareException ex)
        {
            LogLightingApplyFailed(_log, ex, reason);
        }
    }

    /// <summary>Confere a luz e regrava se outro programa trocou (ver <see cref="LightingRecheck"/>).</summary>
    private void EnsureLighting(string reason)
    {
        try
        {
            lock (_gate)
            {
                if (_lighting.EnsureApplied(LightingAllowed))
                {
                    LogLightingRestored(_log, reason, _lighting.Settings.Lighting);
                }
            }
        }
        catch (FirmwareException ex)
        {
            LogLightingApplyFailed(_log, ex, reason);
        }
    }

    /// <summary>Entrada na sessão (login, desbloqueio): o serviço de luz da Acer pode regravar a luz dele.</summary>
    public void SessionStarted(string reason)
    {
        lock (_gate)
        {
            _lightingRecheck.Start(reason);
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
        _gpuOffsets.Recheck(); // confere se o driver manteve o offset
        _lightingRecheck.Start("retomada da suspensão");
    });

    /// <summary>Melhor esforço para devolver o Auto (desligamento, exceção não tratada).</summary>
    public void RestoreAutoNow(string reason)
    {
        Guarded(reason, _engine.RestoreAuto);
        ReleaseGpuOffset(reason);
    }

    /// <summary>Zera o offset da GPU que o killer-sense gravou (melhor esforço; reiniciar o PC também desfaz).</summary>
    private void ReleaseGpuOffset(string reason)
    {
        try
        {
            lock (_gate)
            {
                _gpuOffsets.Release(reason);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogGpuOffsetFailed(_log, ex, reason);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var heartbeat = Heartbeat.CreateOrOpen(_heartbeat.Name);
        RestoreAutoNow("início do serviço");
        if (_control?.IsDisabled == true)
        {
            Guarded("início desativado", () => _engine.Disable("killer-sense encerrado pelo usuário: fans no Auto do EC, PredatorSense no controle."));
        }
        LogStarted(_log);
        ApplyLightingIfEnabled("início do serviço");
        SessionStarted("início do serviço"); // o serviço da Acer pode gravar a luz dele depois do nosso início

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var sw = Stopwatch.StartNew();
        var lastWall = DateTime.UtcNow;
        var ipcLoop = Task.Run(() => _ipc.RunAsync(stoppingToken), stoppingToken);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var dt = sw.Elapsed.TotalSeconds;
                sw.Restart();
                var wallNow = DateTime.UtcNow;
                var gap = Math.Max(dt, (wallNow - lastWall).TotalSeconds); // o relógio de parede cobre o standby
                lastWall = wallNow;
                var woke = LightingRecheck.IsWakeGap(gap);
                var gpuReading = _gpu.Read(); // não acorda a GPU desligada
                var gpuTemp = _gpuFeed.Update(gpuReading);
                EngineSnapshot snap;
                bool? chargeLimit;
                GpuOffsetStatus gpuOffset;
                bool recheckLighting;
                string? recheckReason;
                lock (_gate)
                {
                    if (woke)
                    {
                        _lightingRecheck.Start("volta do standby");
                    }

                    recheckLighting = !woke && _lightingRecheck.Tick(dt);
                    recheckReason = _lightingRecheck.Reason;

                    snap = _engine.Tick(dt, gpuTemp);
                    var batteryAllowed = !_engine.IsDisabled && !_suspended; // desativado/suspenso: nenhuma chamada
                    _chargeLimit.Tick(dt, batteryAllowed);
                    _gpuOffsets.Tick(dt, snap.OperatingMode, gpuReading, allowed: batteryAllowed); // só com a GPU ligada
                    gpuOffset = _gpuOffsets.Status;
                    chargeLimit = batteryAllowed ? _chargeLimit.IsOn : null;
                }

                if (woke)
                {
                    LogWakeGap(_log, (long)gap);
                }

                if (recheckLighting)
                {
                    EnsureLighting(recheckReason ?? "conferência");
                }

                heartbeat.Beat(DateTime.UtcNow, snap.ManualActive);
                _ipc.Publish(ServiceState.From(snap, DateTime.UtcNow, _power(), chargeLimit, gpuReading.Telemetry, gpuOffset));
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Offset da GPU: {Message}")]
    private static partial void LogGpuOffset(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Configuração do offset da GPU ignorada, usando o padrão (desligado): {Problem}")]
    private static partial void LogGpuOffsetConfigIgnored(ILogger logger, string problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Configuração do offset da GPU recusada: {Reason}")]
    private static partial void LogGpuOffsetRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuração do offset da GPU salva: ligado={Enabled}, Turbo {Turbo}.")]
    private static partial void LogGpuOffsetSaved(ILogger logger, bool enabled, GpuClockOffset turbo);

    [LoggerMessage(Level = LogLevel.Critical, Message = "FALHA ao zerar o offset da GPU ({Reason}).")]
    private static partial void LogGpuOffsetFailed(ILogger logger, Exception ex, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Configuração da luz recusada: {Reason}")]
    private static partial void LogLightingRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Configuração da luz salva: controle ligado={Enabled}, {Lighting}.")]
    private static partial void LogLightingSaved(ILogger logger, bool enabled, KeyboardLighting lighting);

    [LoggerMessage(Level = LogLevel.Information, Message = "Luz do teclado gravada ({Reason}): {Lighting}.")]
    private static partial void LogLightingApplied(ILogger logger, string reason, KeyboardLighting lighting);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao gravar a luz do teclado ({Reason}).")]
    private static partial void LogLightingApplyFailed(ILogger logger, Exception ex, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao ler a luz do teclado.")]
    private static partial void LogLightingReadFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Luz do teclado regravada ({Reason}): outro programa tinha trocado a luz (provavelmente o serviço de luz da Acer). Agora: {Lighting}.")]
    private static partial void LogLightingRestored(ILogger logger, string reason, KeyboardLighting lighting);

    [LoggerMessage(Level = LogLevel.Information, Message = "O serviço ficou parado {Seconds} s (standby ou hibernação).")]
    private static partial void LogWakeGap(ILogger logger, long seconds);

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
