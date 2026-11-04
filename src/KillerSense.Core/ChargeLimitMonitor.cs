namespace KillerSense.Core;

/// <summary>
/// Acompanha o limite de carga (~80%) da bateria. A leitura vai por SMI, então é espaçada
/// (<see cref="RefreshSeconds"/>); a troca é a pedido do usuário e confirmada por leitura. Não "segura"
/// a opção: se o Acer Care Center mudar depois, vale a mudança dele (teste A8, 2026-11-06).
/// </summary>
public sealed class ChargeLimitMonitor
{
    /// <summary>Intervalo entre leituras automáticas.</summary>
    public const double RefreshSeconds = 60;

    private readonly IAcerFirmware _fw;
    private double _sinceRead = double.PositiveInfinity; // lê no primeiro ciclo permitido

    public ChargeLimitMonitor(IAcerFirmware firmware)
    {
        ArgumentNullException.ThrowIfNull(firmware);
        _fw = firmware;
    }

    /// <summary>true/false = estado lido; null = ainda não lido, sem suporte ou leitura falhou.</summary>
    public bool? IsOn { get; private set; }

    /// <summary>
    /// Chamado a cada ciclo. <paramref name="allowed"/> = false (desativado, suspensão) não chama o firmware
    /// e esquece o estado, para a próxima leitura ser imediata.
    /// </summary>
    public void Tick(double elapsedSeconds, bool allowed)
    {
        if (!allowed)
        {
            _sinceRead = double.PositiveInfinity;
            return;
        }

        if (double.IsFinite(elapsedSeconds) && elapsedSeconds > 0)
        {
            _sinceRead += elapsedSeconds;
        }

        if (_sinceRead >= RefreshSeconds)
        {
            Refresh();
        }
    }

    /// <summary>Lê agora. Falha de leitura vira estado desconhecido (sem exceção).</summary>
    public void Refresh()
    {
        _sinceRead = 0;
        try
        {
            var h = _fw.GetBatteryHealth();
            IsOn = h.ChargeLimitSupported ? h.ChargeLimitOn : null;
        }
        catch (FirmwareException)
        {
            IsOn = null;
        }
    }

    /// <summary>Liga/desliga e confirma por leitura.</summary>
    /// <exception cref="FirmwareException">O firmware recusou ou não confirmou.</exception>
    public void Set(bool enabled)
    {
        _fw.SetBatteryChargeLimit(enabled);
        Refresh();
        if (IsOn != enabled)
        {
            throw new FirmwareException($"O firmware não confirmou o limite de carga {(enabled ? "ligado" : "desligado")}.");
        }
    }
}
