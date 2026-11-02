using KillerSense.Core;

namespace KillerSense.Ipc;

/// <summary>Ponto de curva serializável.</summary>
public sealed record CurvePointDto(double Temperature, double Percent);

/// <summary>
/// Perfil de fans serializável (pipe e config.json). Sempre validado por <see cref="TryToProfile"/>
/// antes de chegar ao motor — no serviço, nunca se confia no que veio da interface ou do disco.
/// </summary>
public sealed record FanProfileDto(
    IReadOnlyList<CurvePointDto> CpuCurve,
    IReadOnlyList<CurvePointDto> GpuCurve,
    double CpuEntryTemperature,
    double GpuEntryTemperature,
    double HysteresisCelsius,
    double RampUpPercentPerSecond,
    double RampDownPercentPerSecond)
{
    public const double MinEntryTemperature = 30;
    public const double MaxEntryTemperature = 90;

    public static FanProfileDto From(FanProfile p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new(
            [.. p.CpuCurve.Points.Select(x => new CurvePointDto(x.Temperature, x.Percent))],
            [.. p.GpuCurve.Points.Select(x => new CurvePointDto(x.Temperature, x.Percent))],
            p.CpuEntryTemperature,
            p.GpuEntryTemperature,
            p.Tuning.HysteresisCelsius,
            p.Tuning.RampUpPercentPerSecond,
            p.Tuning.RampDownPercentPerSecond);
    }

    /// <summary>Converte e valida. Devolve null e o motivo em <paramref name="error"/> se algo estiver errado.</summary>
    public FanProfile? TryToProfile(out string? error)
    {
        error = Check("Curva da CPU", CpuCurve) ?? Check("Curva da GPU", GpuCurve)
            ?? CheckEntry("CPU", CpuEntryTemperature) ?? CheckEntry("GPU", GpuEntryTemperature);
        var tuning = new FanTuning
        {
            HysteresisCelsius = HysteresisCelsius,
            RampUpPercentPerSecond = RampUpPercentPerSecond,
            RampDownPercentPerSecond = RampDownPercentPerSecond,
        };
        error ??= tuning.Validate();
        if (error is not null)
        {
            return null;
        }

        return new FanProfile(
            FanCurve.Create(CpuCurve.Select(x => new CurvePoint(x.Temperature, x.Percent))),
            FanCurve.Create(GpuCurve.Select(x => new CurvePoint(x.Temperature, x.Percent))),
            tuning,
            CpuEntryTemperature,
            GpuEntryTemperature);
    }

    private static string? Check(string name, IReadOnlyList<CurvePointDto>? points)
    {
        if (points is null)
        {
            return $"{name}: ausente.";
        }

        var e = FanCurve.Validate([.. points.Select(x => new CurvePoint(x.Temperature, x.Percent))]);
        return e is null ? null : $"{name}: {e}";
    }

    private static string? CheckEntry(string fan, double t) =>
        double.IsFinite(t) && t is >= MinEntryTemperature and <= MaxEntryTemperature
            ? null
            : $"Temperatura de entrada da {fan} precisa estar entre {MinEntryTemperature} e {MaxEntryTemperature} °C.";
}
