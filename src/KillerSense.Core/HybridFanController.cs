namespace KillerSense.Core;

/// <summary>Decisão de um ciclo para uma fan: Auto do EC, ou Custom com o % a gravar.</summary>
public readonly record struct FanDecision(FanMode Mode, int? Percent)
{
    public static FanDecision Auto { get; } = new(FanMode.Auto, null);

    public static FanDecision Custom(int percent) => new(FanMode.Custom, percent);
}

/// <summary>
/// Modo híbrido de uma fan (docs/arquitetura.md §5.4.1): abaixo da temperatura de entrada deixa o
/// Auto do EC (mais silencioso em repouso); a partir dela a curva assume. Volta ao Auto quando a
/// temperatura cai <c>exitMargin</c> abaixo da entrada E a curva já chegou ao piso.
/// </summary>
public sealed class HybridFanController
{
    private readonly FanCurveController _curve;
    private readonly FanTuning _tuning;
    private readonly double _entry;
    private readonly double _exit;

    public HybridFanController(FanCurve curve, FanTuning tuning, double entryTemperature, double exitMargin = SafetyPolicy.DefaultExitMargin)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        if (!double.IsFinite(entryTemperature) || entryTemperature is < 30 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(entryTemperature), "A temperatura de entrada precisa estar entre 30 e 90 °C.");
        }

        if (!double.IsFinite(exitMargin) || exitMargin is < 2 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(exitMargin), "A margem de saída precisa estar entre 2 e 20 °C.");
        }

        _curve = new FanCurveController(curve, tuning);
        _tuning = tuning;
        _entry = entryTemperature;
        _exit = entryTemperature - exitMargin;
    }

    /// <summary>Verdadeiro enquanto a curva controla a fan (Custom).</summary>
    public bool IsCurveActive { get; private set; }

    public FanDecision Step(double temperature, OperatingMode mode, double elapsedSeconds)
    {
        if (!double.IsFinite(temperature))
        {
            throw new ArgumentOutOfRangeException(nameof(temperature), "Temperatura precisa ser um número.");
        }

        if (!IsCurveActive)
        {
            if (temperature < _entry)
            {
                return FanDecision.Auto;
            }

            IsCurveActive = true;
            _curve.Reset();
        }

        var percent = _curve.Step(temperature, mode, elapsedSeconds);
        var floor = Math.Max(SafetyPolicy.FloorPercent(mode), _tuning.MinPercent);
        if (temperature <= _exit && _curve.Output <= floor + 0.5)
        {
            IsCurveActive = false;
            _curve.Reset();
            return FanDecision.Auto;
        }

        return FanDecision.Custom(percent);
    }

    /// <summary>Volta ao estado inicial (Auto), ex.: depois de um failsafe.</summary>
    public void Reset()
    {
        IsCurveActive = false;
        _curve.Reset();
    }
}
