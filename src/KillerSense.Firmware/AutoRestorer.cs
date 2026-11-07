using KillerSense.Core;

namespace KillerSense.Firmware;

/// <summary>
/// Acesso mínimo para o watchdog: a ÚNICA operação é devolver as fans ao Auto do EC
/// (<c>SetGamingFanBehavior(0x00410009)</c>) e conferir por leitura.
/// </summary>
public sealed class AutoRestorer : IDisposable
{
    private static readonly Dictionary<Fan, FanMode> AutoBoth = new() { [Fan.Cpu] = FanMode.Auto, [Fan.Gpu] = FanMode.Auto };
    private readonly AcerWmiFirmware _fw = new();

    /// <summary>Aplica o Auto e devolve se o firmware confirmou nas duas fans.</summary>
    public bool RestoreAuto()
    {
        _fw.SetFanModes(AutoBoth);
        return _fw.GetFanModes() == (FanMode.Auto, FanMode.Auto);
    }

    public void Dispose() => _fw.Dispose();
}
