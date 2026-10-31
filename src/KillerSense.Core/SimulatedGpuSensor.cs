namespace KillerSense.Core;

/// <summary>GPU simulada (modo --simulated e testes): por padrão desligada.</summary>
public sealed class SimulatedGpuSensor : IGpuSensor
{
    public GpuReading Next { get; set; } = GpuReading.Off;

    public GpuReading Read() => Next;
}
