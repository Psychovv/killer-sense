namespace KillerSense.Core;

/// <summary>
/// Luz do teclado no serviço. Com o controle DESLIGADO (padrão) não há nenhuma chamada ao firmware da luz:
/// a configuração só é guardada. Ligado, grava a pedido e ao iniciar o serviço, e confere lendo de volta.
/// Não "segura" a luz: se o PredatorSense ou o modo Eco mudarem depois, vale a mudança deles até o próximo pedido.
/// </summary>
public sealed class LightingController
{
    private readonly IAcerFirmware _fw;

    public LightingController(IAcerFirmware firmware, LightingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(firmware);
        ArgumentNullException.ThrowIfNull(settings);
        _fw = firmware;
        Settings = settings.Validate() is null ? settings : LightingSettings.Default;
    }

    public LightingSettings Settings { get; private set; }

    /// <summary>
    /// Troca a configuração. Com o controle ligado e <paramref name="allowed"/> (killer-sense ativo, fora da
    /// suspensão), grava no teclado e confere. A configuração fica trocada mesmo se a gravação falhar.
    /// </summary>
    /// <exception cref="ArgumentException">Configuração fora das faixas.</exception>
    /// <exception cref="FirmwareException">O firmware recusou ou a leitura não bateu com o pedido.</exception>
    public void SetSettings(LightingSettings settings, bool allowed)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Validate() is { } error)
        {
            throw new ArgumentException(error, nameof(settings));
        }

        Settings = settings;
        if (settings.Enabled && allowed)
        {
            Apply();
        }
    }

    /// <summary>Grava a configuração atual se o controle estiver ligado. False = nada a fazer.</summary>
    /// <exception cref="FirmwareException">O firmware recusou ou a leitura não bateu com o pedido.</exception>
    public bool ApplyIfEnabled(bool allowed)
    {
        if (!Settings.Enabled || !allowed)
        {
            return false;
        }

        Apply();
        return true;
    }

    /// <summary>O que o teclado tem agora (só com o controle ligado e permitido); senão null.</summary>
    /// <exception cref="FirmwareException">A leitura falhou.</exception>
    public KeyboardLighting? ReadCurrent(bool allowed) =>
        Settings.Enabled && allowed ? _fw.GetKeyboardLighting() : null;

    private void Apply()
    {
        var wanted = Settings.Lighting;
        _fw.SetKeyboardLighting(wanted);
        var now = _fw.GetKeyboardLighting();
        if (!wanted.SameVisibleAs(now))
        {
            throw new FirmwareException($"O teclado não confirmou a luz. Pedido: {wanted}. Lido: {now}.");
        }
    }
}
