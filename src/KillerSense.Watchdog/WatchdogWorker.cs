using KillerSense.Core;
using KillerSense.Firmware;

namespace KillerSense.Watchdog;

/// <summary>
/// Confere o batimento do serviço a cada 500 ms. Se o serviço parar de bater por mais de 3 s com fan em
/// manual (Custom ou Max), devolve o Auto do EC e limpa a marca de manual (docs/arquitetura.md §6.3).
/// </summary>
public sealed partial class WatchdogWorker : BackgroundService
{
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(3);
    private readonly ILogger<WatchdogWorker> _log;

    public WatchdogWorker(ILogger<WatchdogWorker> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_log);
        using var restorer = new AutoRestorer();
        Heartbeat? hb = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                hb ??= Heartbeat.TryOpen();
                if (hb is null)
                {
                    continue; // serviço ainda não subiu
                }

                var (last, manual) = hb.Read();
                var age = DateTime.UtcNow - last;
                if (!manual || age <= Stale)
                {
                    continue;
                }

                LogStale(_log, age.TotalSeconds);
                try
                {
                    var ok = restorer.RestoreAuto();
                    hb.ClearManual();
                    LogRestored(_log, ok);
                }
                catch (FirmwareException ex)
                {
                    LogRestoreFailed(_log, ex); // tenta de novo no próximo ciclo
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // parada normal
        }
        finally
        {
            hb?.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "watchdog iniciado.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Serviço sem batimento há {Seconds:0.0} s com fan em manual; devolvendo o Auto do EC.")]
    private static partial void LogStale(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auto do EC aplicado pelo watchdog (confirmado: {Confirmed}).")]
    private static partial void LogRestored(ILogger logger, bool confirmed);

    [LoggerMessage(Level = LogLevel.Critical, Message = "FALHA do watchdog ao aplicar o Auto do EC.")]
    private static partial void LogRestoreFailed(ILogger logger, Exception ex);
}
