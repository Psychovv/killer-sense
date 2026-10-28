namespace KillerSense.Core;

/// <summary>Parâmetros de suavização de uma fan em curva (docs/arquitetura.md §5.2).</summary>
public sealed record FanTuning
{
    /// <summary>Só baixa a rotação quando a temperatura cai esta quantia abaixo da que fez subir.</summary>
    public double HysteresisCelsius { get; init; } = 3;

    /// <summary>Subida máxima por segundo (%/s).</summary>
    public double RampUpPercentPerSecond { get; init; } = 15;

    /// <summary>Descida máxima por segundo (%/s).</summary>
    public double RampDownPercentPerSecond { get; init; } = 3;

    /// <summary>Teto definido pelo usuário (%).</summary>
    public double MaxPercent { get; init; } = 100;

    /// <summary>Piso definido pelo usuário (%). O piso de segurança do modo térmico sempre prevalece se for maior.</summary>
    public double MinPercent { get; init; }

    public string? Validate()
    {
        if (!double.IsFinite(HysteresisCelsius) || HysteresisCelsius is < 0 or > 15)
        {
            return "Histerese precisa estar entre 0 e 15 °C.";
        }

        if (!double.IsFinite(RampUpPercentPerSecond) || RampUpPercentPerSecond is < 1 or > 100)
        {
            return "Rampa de subida precisa estar entre 1 e 100 %/s.";
        }

        if (!double.IsFinite(RampDownPercentPerSecond) || RampDownPercentPerSecond is < 1 or > 100)
        {
            return "Rampa de descida precisa estar entre 1 e 100 %/s.";
        }

        if (!double.IsFinite(MinPercent) || !double.IsFinite(MaxPercent) || MinPercent < 0 || MaxPercent > 100 || MinPercent > MaxPercent)
        {
            return "Limites de rotação precisam estar em 0–100% com mínimo ≤ máximo.";
        }

        return null;
    }
}

/// <summary>
/// Calcula a rotação de UMA fan em curva a cada ciclo: curva → histerese → limites/pisos → rampa.
/// Não fala com hardware; só devolve o % inteiro a aplicar.
/// </summary>
public sealed class FanCurveController
{
    private readonly FanCurve _curve;
    private readonly FanTuning _tuning;
    private double? _effectiveTemperature;
    private double? _output;

    public FanCurveController(FanCurve curve, FanTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(tuning);
        var error = tuning.Validate();
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(tuning));
        }

        _curve = curve;
        _tuning = tuning;
    }

    /// <summary>Último % calculado (null antes do primeiro ciclo).</summary>
    public double? Output => _output;

    /// <summary>
    /// Um ciclo do laço.
    /// </summary>
    /// <param name="temperature">Temperatura válida (já filtrada).</param>
    /// <param name="mode">Modo térmico atual (define o piso de segurança).</param>
    /// <param name="elapsedSeconds">Tempo desde o ciclo anterior (limitado a 0,1–5 s).</param>
    /// <returns>% inteiro (0–100) a gravar no EC.</returns>
    public int Step(double temperature, OperatingMode mode, double elapsedSeconds)
    {
        if (!double.IsFinite(temperature))
        {
            throw new ArgumentOutOfRangeException(nameof(temperature), "Temperatura precisa ser um número.");
        }

        var dt = Math.Clamp(double.IsFinite(elapsedSeconds) ? elapsedSeconds : 1, 0.1, 5);

        // Histerese: a temperatura "efetiva" sobe na hora e só desce quando cai mais que a histerese.
        if (_effectiveTemperature is not { } eff || temperature >= eff)
        {
            _effectiveTemperature = temperature;
        }
        else if (temperature < eff - _tuning.HysteresisCelsius)
        {
            _effectiveTemperature = temperature + _tuning.HysteresisCelsius;
        }

        var floor = Math.Max(SafetyPolicy.FloorPercent(mode), _tuning.MinPercent);
        var ceiling = Math.Max(_tuning.MaxPercent, floor);
        var target = Math.Clamp(_curve.Evaluate(_effectiveTemperature.Value), floor, ceiling);

        double next;
        if (_output is not { } prev)
        {
            next = target; // primeiro ciclo: vai direto (o serviço parte do Auto do EC)
        }
        else if (target > prev)
        {
            next = Math.Min(target, prev + (_tuning.RampUpPercentPerSecond * dt));
        }
        else
        {
            next = Math.Max(target, prev - (_tuning.RampDownPercentPerSecond * dt));
        }

        // O piso de segurança vale mesmo durante a rampa de descida (ex.: troca para Turbo).
        next = Math.Clamp(next, floor, 100);
        _output = next;
        return (int)Math.Round(next, MidpointRounding.AwayFromZero);
    }

    /// <summary>Esquece o estado (ex.: depois de voltar ao Auto).</summary>
    public void Reset()
    {
        _effectiveTemperature = null;
        _output = null;
    }
}
