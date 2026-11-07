using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace KillerSense.Service;

/// <summary>
/// Ciclo de vida como serviço do Windows, com tratamento de suspensão/retomada e desligamento.
/// Só é usado quando o processo roda sob o Gerenciador de Serviços.
/// </summary>
public sealed class KillerSenseServiceLifetime : WindowsServiceLifetime
{
    private readonly FanControlWorker _worker;

    public KillerSenseServiceLifetime(
        IHostEnvironment environment,
        IHostApplicationLifetime applicationLifetime,
        ILoggerFactory loggerFactory,
        IOptions<HostOptions> optionsAccessor,
        IOptions<WindowsServiceLifetimeOptions> windowsServiceOptionsAccessor,
        FanControlWorker worker)
        : base(environment, applicationLifetime, loggerFactory, optionsAccessor, windowsServiceOptionsAccessor)
    {
        _worker = worker;
        CanHandlePowerEvent = true;
        CanHandleSessionChangeEvent = true;
        CanShutdown = true;
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        // Ao entrar na sessão o serviço de luz da Acer pode regravar a luz dele: confere a do killer-sense.
        if (changeDescription.Reason is SessionChangeReason.SessionLogon or SessionChangeReason.SessionUnlock)
        {
            _worker.SessionStarted(changeDescription.Reason == SessionChangeReason.SessionLogon ? "login" : "desbloqueio");
        }

        base.OnSessionChange(changeDescription);
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        switch (powerStatus)
        {
            case PowerBroadcastStatus.Suspend:
                _worker.Suspend();
                break;
            case PowerBroadcastStatus.ResumeSuspend:
            case PowerBroadcastStatus.ResumeAutomatic:
                _worker.Resume();
                break;
        }

        return base.OnPowerEvent(powerStatus);
    }

    protected override void OnShutdown()
    {
        _worker.RestoreAutoNow("desligamento do Windows");
        base.OnShutdown();
    }
}
