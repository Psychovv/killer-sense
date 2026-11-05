namespace KillerSense.Core;

/// <summary>Uma leitura da GPU dedicada: desligada (Optimus), temperatura, ou erro com ela ligada. Telemetry só vem com ela ligada.</summary>
public readonly record struct GpuReading(bool PoweredOn, double? Temperature, GpuTelemetry? Telemetry = null)
{
    public static GpuReading Off { get; } = new(false, null);

    public static GpuReading Error { get; } = new(true, null);

    public static GpuReading Value(double celsius, GpuTelemetry? telemetry = null) => new(true, celsius, telemetry);
}

/// <summary>Fonte de leituras da GPU (NVML no serviço; simulada nos testes).</summary>
public interface IGpuSensor
{
    /// <summary>Lê sem acordar a GPU: se ela estiver desligada, devolve <see cref="GpuReading.Off"/>.</summary>
    GpuReading Read();
}

/// <summary>
/// Transforma as leituras da GPU na temperatura que o motor usa: GPU desligada ⇒ null sem contar como
/// falha (fan da GPU no Auto do EC); erro ou valor inválido com ela ligada ⇒ conta como inválido.
/// </summary>
public sealed class GpuTemperatureFeed
{
    private SensorFilter _filter = SensorFilter.ForTemperature();

    /// <summary>Temperatura válida (mediana de 3) ou null (desligada, ainda sem leitura ou sensor em falha).</summary>
    public double? Update(GpuReading reading)
    {
        if (!reading.PoweredOn)
        {
            _filter = SensorFilter.ForTemperature(); // ao religar, recomeça sem histórico velho
            return null;
        }

        _filter.Add(reading.Temperature);
        return _filter.Value;
    }

    /// <summary>Verdadeiro quando a GPU está ligada mas as leituras falharam demais.</summary>
    public bool IsFaulted => _filter.IsFaulted;
}

/// <summary>
/// Dados de exibição da GPU (painel da tela Modos). Não entram no controle das fans. Cada campo é null
/// quando o driver não informa.
/// </summary>
public sealed record GpuTelemetry(
    int? CoreClockMhz,
    int? MemoryClockMhz,
    int? UtilizationPercent,
    double? PowerWatts,
    double? PowerLimitWatts,
    int? PerformanceState);
