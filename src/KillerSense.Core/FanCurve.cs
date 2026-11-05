namespace KillerSense.Core;

/// <summary>Ponto da curva: temperatura (°C) → rotação (%).</summary>
public readonly record struct CurvePoint(double Temperature, double Percent);

/// <summary>
/// Curva de fan: de 2 a 10 pontos, com temperatura estritamente crescente e % não decrescente,
/// dentro de 0–100. Fora da faixa dos pontos, o valor fica no primeiro/último ponto.
/// </summary>
public sealed class FanCurve
{
    public const int MinPoints = 2;
    public const int MaxPoints = 10;

    public IReadOnlyList<CurvePoint> Points { get; }

    private FanCurve(IReadOnlyList<CurvePoint> points) => Points = points;

    /// <summary>Cria a curva ou lança <see cref="ArgumentException"/> com o motivo.</summary>
    public static FanCurve Create(IEnumerable<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var list = points.ToArray();
        var error = Validate(list);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(points));
        }

        return new FanCurve(list);
    }

    /// <summary>Devolve <c>null</c> se a curva é válida, ou a mensagem do problema.</summary>
    public static string? Validate(IReadOnlyList<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count is < MinPoints or > MaxPoints)
        {
            return $"A curva precisa de {MinPoints} a {MaxPoints} pontos.";
        }

        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (!double.IsFinite(p.Temperature) || !double.IsFinite(p.Percent))
            {
                return $"Ponto {i + 1} tem valor inválido.";
            }

            if (p.Temperature is < PhysicalLimits.MinTemperature or > PhysicalLimits.MaxTemperature)
            {
                return $"Ponto {i + 1}: temperatura fora de {PhysicalLimits.MinTemperature}–{PhysicalLimits.MaxTemperature} °C.";
            }

            if (p.Percent is < 0 or > 100)
            {
                return $"Ponto {i + 1}: rotação fora de 0–100%.";
            }

            if (i > 0 && p.Temperature <= points[i - 1].Temperature)
            {
                return $"Ponto {i + 1}: a temperatura precisa ser maior que a do ponto anterior.";
            }

            if (i > 0 && p.Percent < points[i - 1].Percent)
            {
                return $"Ponto {i + 1}: a rotação não pode diminuir quando a temperatura sobe.";
            }
        }

        return null;
    }

    /// <summary>
    /// Pontos com um a mais em <paramref name="temperature"/> (arredondada para °C inteiro), com a %
    /// que a curva já tinha ali, ou seja, sem mudar a forma. Null se não cabe: limite de pontos, já
    /// existe um ponto nessa temperatura ou o resultado seria inválido.
    /// </summary>
    public static CurvePoint[]? WithPointAt(IReadOnlyList<CurvePoint> points, double temperature)
    {
        ArgumentNullException.ThrowIfNull(points);
        var t = Math.Round(temperature);
        if (points.Count is < MinPoints or >= MaxPoints || !double.IsFinite(t) || points.Any(p => Math.Abs(p.Temperature - t) < 1))
        {
            return null;
        }

        var list = points.ToList();
        var index = list.Count(p => p.Temperature < t);
        var percent = index == 0 ? list[0].Percent
            : index == list.Count ? list[^1].Percent
            : list[index - 1].Percent + ((list[index].Percent - list[index - 1].Percent) * (t - list[index - 1].Temperature) / (list[index].Temperature - list[index - 1].Temperature));
        list.Insert(index, new CurvePoint(t, Math.Round(percent)));
        return Validate(list) is null ? [.. list] : null;
    }

    /// <summary>Interpolação linear da % para a temperatura dada.</summary>
    public double Evaluate(double temperature)
    {
        var p = Points;
        if (temperature <= p[0].Temperature)
        {
            return p[0].Percent;
        }

        for (var i = 1; i < p.Count; i++)
        {
            if (temperature <= p[i].Temperature)
            {
                var a = p[i - 1];
                var b = p[i];
                return a.Percent + ((b.Percent - a.Percent) * (temperature - a.Temperature) / (b.Temperature - a.Temperature));
            }
        }

        return p[^1].Percent;
    }
}
