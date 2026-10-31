using System.Diagnostics;
using KillerSense.Core;

namespace KillerSense.Service;

/// <summary>Nome da memória compartilhada do batimento (o simulado usa um nome local, sem precisar de admin).</summary>
public sealed record HeartbeatOptions(string Name);

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
    private readonly ILogger<FanControlWorker> _log;
    private readonly Lock _gate = new();
    private (EngineState State, FanMode Cpu, FanMode Gpu)? _lastLogged;

    public FanControlWorker(FanControlEngine engine, IGpuSensor gpu, HeartbeatOptions heartbeat, ILogger<FanControlWorker> log)
    {
        _engine = engine;
        _gpu = gpu;
        _heartbeat = heartbeat;
        _log = log;
    }

    /// <summary>Suspensão do Windows: Auto antes de dormir e controle pausado.</summary>
    public void Suspend() => Guarded("suspensão", () => _engine.Pause("Sistema em suspensão."));

    /// <summary>Retomada: recomeça do Auto e libera o controle.</summary>
    public void Resume() => Guarded("retomada", () =>
    {
        _engine.RestoreAuto();
        _engine.Resume();
    });

    /// <summary>Melhor esforço para devolver o Auto (desligamento, exceção não tratada).</summary>
    public void RestoreAutoNow(string reason) => Guarded(reason, _engine.RestoreAuto);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var heartbeat = Heartbeat.CreateOrOpen(_heartbeat.Name);
        RestoreAutoNow("início do serviço");
        LogStarted(_log);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var sw = Stopwatch.StartNew();
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                var dt = sw.Elapsed.TotalSeconds;
                sw.Restart();
                var gpuTemp = _gpuFeed.Update(_gpu.Read()); // não acorda a GPU desligada
                EngineSnapshot snap;
                lock (_gate)
                {
                    snap = _engine.Tick(dt, gpuTemp);
                }

                heartbeat.Beat(DateTime.UtcNow, snap.ManualActive);
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
