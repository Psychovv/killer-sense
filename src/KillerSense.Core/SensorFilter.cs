namespace KillerSense.Core;

/// <summary>
/// Valida as leituras de um sensor e entrega a mediana das últimas 3 válidas.
/// Conta leituras inválidas seguidas (0, fora da faixa, NaN, erro) — docs/arquitetura.md §6.2.
/// </summary>
public sealed class SensorFilter
{
    private const int Window = 3;
    private readonly double _min;
    private readonly double _max;
    private readonly bool _zeroIsValid;
    private readonly Queue<double> _recent = new(Window);

    /// <param name="min">Menor valor válido.</param>
    /// <param name="max">Maior valor válido.</param>
    /// <param name="zeroIsValid">RPM 0 pode ser real (fan parada); temperatura 0 nunca é.</param>
    public SensorFilter(double min, double max, bool zeroIsValid = false)
    {
        if (!(min <= max))
        {
            throw new ArgumentOutOfRangeException(nameof(min), "min precisa ser ≤ max.");
        }

        _min = min;
        _max = max;
        _zeroIsValid = zeroIsValid;
    }

    public static SensorFilter ForTemperature() => new(PhysicalLimits.MinTemperature, PhysicalLimits.MaxTemperature);

    public static SensorFilter ForFanRpm() => new(0, PhysicalLimits.MaxFanRpm, zeroIsValid: true);

    /// <summary>Leituras inválidas seguidas desde a última válida.</summary>
    public int ConsecutiveInvalid { get; private set; }

    /// <summary>Verdadeiro quando já houve leituras inválidas demais seguidas.</summary>
    public bool IsFaulted => ConsecutiveInvalid >= SafetyPolicy.MaxConsecutiveInvalidReadings;

    /// <summary>Mediana das últimas leituras válidas, ou null se ainda não houver nenhuma ou se o sensor está em falha.</summary>
    public double? Value => IsFaulted || _recent.Count == 0 ? null : Median();

    /// <summary>Registra uma leitura (null = erro ao ler). Devolve se ela foi aceita.</summary>
    public bool Add(double? reading)
    {
        if (reading is not { } v || !IsValid(v))
        {
            ConsecutiveInvalid++;
            if (IsFaulted)
            {
                _recent.Clear();
            }

            return false;
        }

        ConsecutiveInvalid = 0;
        if (_recent.Count == Window)
        {
            _recent.Dequeue();
        }

        _recent.Enqueue(v);
        return true;
    }

    private bool IsValid(double v) =>
        double.IsFinite(v) && (v != 0 || _zeroIsValid) && v >= _min && v <= _max;

    private double Median()
    {
        var sorted = _recent.Order().ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
