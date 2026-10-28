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
}
