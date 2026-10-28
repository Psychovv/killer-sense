using KillerSense.Core;

namespace KillerSense.Firmware;

/// <summary>
/// Firmware simulado para testes e para desenvolver a interface sem hardware.
/// Imita o comportamento observado: Auto/Max/Custom por fan, % guardado no EC mesmo em Auto,
/// RPM aproximado e sensores configuráveis (inclusive leituras inválidas).
/// </summary>
public sealed class SimulatedFirmware : IAcerFirmware
{
    private readonly Dictionary<Fan, FanMode> _modes = new() { [Fan.Cpu] = FanMode.Auto, [Fan.Gpu] = FanMode.Auto };
    private readonly Dictionary<Fan, int> _speeds = new() { [Fan.Cpu] = 50, [Fan.Gpu] = 50 };
    private readonly Dictionary<Sensor, int> _sensors = new()
    {
        [Sensor.CpuTemperature] = 55,
        [Sensor.Ext1Temperature] = 45,
        [Sensor.SkinTemperature] = 48,
        [Sensor.Gpu2FanRpm] = 0,
    };

    private readonly List<string> _calls = [];

    public OperatingMode OperatingMode { get; set; } = OperatingMode.Balanced;

    /// <summary>Se definido, a próxima chamada a este método lança <see cref="FirmwareException"/>.</summary>
    public string? FailNextCall { get; set; }

    /// <summary>Registro das chamadas feitas (para os testes conferirem).</summary>
    public IReadOnlyList<string> Calls => _calls;

    public void SetSensor(Sensor sensor, int value) => _sensors[sensor] = value;

    public FanMode GetFanMode(Fan fan) => Record($"GetFanMode({fan})", () => _modes[fan]);

    public void SetFanModes(IReadOnlyDictionary<Fan, FanMode> modes)
    {
        var encoded = AcerProtocol.EncodeSetFanBehavior(modes); // mesma validação do real
        Record($"SetFanModes(0x{encoded:X8})", () =>
        {
            foreach (var (fan, mode) in modes)
            {
                _modes[fan] = mode;
            }

            return 0;
        });
    }

    public int GetFanSpeedPercent(Fan fan) => Record($"GetFanSpeedPercent({fan})", () => _speeds[fan]);

    public void SetFanSpeedPercent(Fan fan, int percent)
    {
        _ = AcerProtocol.EncodeSetFanSpeed(fan, percent);
        Record($"SetFanSpeedPercent({fan},{percent})", () => _speeds[fan] = percent);
    }

    public int ReadSensor(Sensor sensor) => Record($"ReadSensor({sensor})", () => sensor switch
    {
        Sensor.CpuFanRpm => Rpm(Fan.Cpu),
        Sensor.GpuFanRpm => Rpm(Fan.Gpu),
        _ => _sensors.GetValueOrDefault(sensor),
    });

    public OperatingMode GetOperatingMode() => Record("GetOperatingMode", () => OperatingMode);

    private int Rpm(Fan fan) => _modes[fan] switch
    {
        FanMode.Max => 6976,
        FanMode.Custom => (int)(_speeds[fan] * 69.76),
        _ => 2000,
    };

    private T Record<T>(string call, Func<T> action)
    {
        _calls.Add(call);
        var name = call.Split('(')[0];
        if (FailNextCall == name)
        {
            FailNextCall = null;
            throw new FirmwareException($"Falha simulada em {name}.");
        }

        return action();
    }
}
