namespace KillerSense.Core;

/// <summary>Erro reportado pelo firmware (status ≠ 0) ou pela chamada WMI.</summary>
public sealed class FirmwareException : Exception
{
    public FirmwareException()
    {
    }

    public FirmwareException(string message)
        : base(message)
    {
    }

    public FirmwareException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Operações de alto nível sobre o firmware da Acer. Implementações: WMI real e simulado.
/// Só operações da lista de permissão existem aqui — docs/arquitetura.md §4.
/// </summary>
public interface IAcerFirmware
{
    /// <summary>Modo atual de uma fan (<c>GetGamingFanBehavior</c>).</summary>
    FanMode GetFanMode(Fan fan);

    /// <summary>
    /// Modo das duas fans numa única chamada (<c>GetGamingFanBehavior(0x09)</c>). Preferir a
    /// <see cref="GetFanMode"/>: cada leitura do modo passa por SMI, que pausa todos os núcleos por um instante.
    /// </summary>
    (FanMode Cpu, FanMode Gpu) GetFanModes();

    /// <summary>Define o modo de uma ou mais fans numa única chamada (<c>SetGamingFanBehavior</c>).</summary>
    void SetFanModes(IReadOnlyDictionary<Fan, FanMode> modes);

    /// <summary>% Custom gravado no EC (<c>GetGamingFanSpeed</c>). Não é a rotação real em Auto.</summary>
    int GetFanSpeedPercent(Fan fan);

    /// <summary>Grava o % Custom de uma fan (<c>SetGamingFanSpeed</c>), 0–100.</summary>
    void SetFanSpeedPercent(Fan fan, int percent);

    /// <summary>Lê um sensor (<c>GetGamingSysInfo</c>). Efeito colateral conhecido: grava EC.PSEE = 1.</summary>
    int ReadSensor(Sensor sensor);

    /// <summary>Modo de operação atual (<c>GetGamingMiscSetting(0x0B)</c>).</summary>
    OperatingMode GetOperatingMode();

    /// <summary>Troca o modo de operação (<c>SetGamingMiscSetting((modo &lt;&lt; 8) | 0x0B)</c>, teste A16).</summary>
    void SetOperatingMode(OperatingMode mode);

    /// <summary>Funções de saúde da bateria 1 (<c>BatteryControl.GetBatteryHealthControlStatus</c>, via SMI).</summary>
    BatteryHealth GetBatteryHealth();

    /// <summary>Liga/desliga o limite de carga (~80%): <c>SetBatteryHealthControl(1, 1, on)</c>. Só a máscara 1.</summary>
    void SetBatteryChargeLimit(bool enabled);

    /// <summary>
    /// Luz do teclado como está no EC: <c>GetGamingKBBacklight(1)</c>, <c>GetGamingRgbKb</c> das 4 zonas e
    /// <c>GetGamingLED(8)</c>. Só leituras do EC, sem SMI.
    /// </summary>
    KeyboardLighting GetKeyboardLighting();

    /// <summary>
    /// Grava a luz do teclado como o PredatorSense faz: zonas acesas (<c>SetGamingLED</c>), efeito e brilho
    /// (<c>SetGamingKBBacklight</c>, só o teclado) e, no estático, a cor de cada zona (<c>SetGamingRgbKb</c>).
    /// </summary>
    void SetKeyboardLighting(KeyboardLighting lighting);
}
