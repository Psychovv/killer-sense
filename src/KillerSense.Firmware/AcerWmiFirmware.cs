using KillerSense.Core;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Generic;
using Microsoft.Management.Infrastructure.Options;

namespace KillerSense.Firmware;

/// <summary>
/// Acesso real ao firmware via WMI (<c>root\WMI</c>). Exige administrador.
/// SÓ chama os métodos da <see cref="Allowlist"/>; qualquer outro nome lança exceção antes de tocar no WMI.
/// As classes perigosas (BIOSSetting, UtilityFunction, DataTrans, MethodForPowerShell,
/// AcerBiosConfigurationTool) não aparecem aqui de propósito.
/// </summary>
public sealed class AcerWmiFirmware : IAcerFirmware, IDisposable
{
    internal const string Namespace = @"root\WMI";
    internal const string GamingClass = "AcerGamingFunction";

    /// <summary>Métodos permitidos de <see cref="GamingClass"/> e o tipo CIM da entrada <c>gmInput</c>.</summary>
    internal static readonly IReadOnlyDictionary<string, CimType> Allowlist = new Dictionary<string, CimType>(StringComparer.Ordinal)
    {
        ["GetGamingFanBehavior"] = CimType.UInt32,
        ["SetGamingFanBehavior"] = CimType.UInt64,
        ["GetGamingFanSpeed"] = CimType.UInt32,
        ["SetGamingFanSpeed"] = CimType.UInt64,
        ["GetGamingSysInfo"] = CimType.UInt32,
        ["GetGamingMiscSetting"] = CimType.UInt32,
        ["SetGamingMiscSetting"] = CimType.UInt64, // só o modo (0x0B): ver AcerProtocol.IsAllowedMiscSetting
        // Luz do teclado. Get: leitura pura do EC. Set: só as zonas/efeito/cor do teclado (AcerProtocol.IsAllowed*).
        ["GetGamingLED"] = CimType.UInt32,
        ["SetGamingLED"] = CimType.UInt8Array,
        ["GetGamingRgbKb"] = CimType.UInt32,
        ["SetGamingRgbKb"] = CimType.UInt64,
        ["GetGamingKBBacklight"] = CimType.UInt32,
        ["SetGamingKBBacklight"] = CimType.UInt8Array,
    };

    /// <summary>Pausa entre as cores das zonas, como a DLL da Acer (30 ms).</summary>
    private static readonly TimeSpan ZoneColorPause = TimeSpan.FromMilliseconds(30);

    internal const string BatteryClass = "BatteryControl";

    /// <summary>Métodos permitidos de <see cref="BatteryClass"/> (as funções BatteryFunctionData ficam de fora).</summary>
    internal static readonly IReadOnlySet<string> BatteryAllowlist = new HashSet<string>(StringComparer.Ordinal)
    {
        "GetBatteryHealthControlStatus",
        "SetBatteryHealthControl",
    };

    /// <summary>Única máscara aceita em SetBatteryHealthControl: bit0 = limite de carga. Calibração (bit1) bloqueada.</summary>
    internal const byte ChargeLimitMask = 0x01;

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    private readonly Lock _gate = new();
    private readonly CimSession _session;
    private readonly CimOperationOptions _options;
    private CimInstance? _instance;
    private CimInstance? _batteryInstance;
    private bool _disposed;

    public AcerWmiFirmware()
    {
        _session = CimSession.Create(null);
        _options = new CimOperationOptions { Timeout = CallTimeout };
    }

    public FanMode GetFanMode(Fan fan) =>
        AcerProtocol.DecodeFanMode(Invoke("GetGamingFanBehavior", AcerProtocol.BehaviorMask([fan])), fan);

    public (FanMode Cpu, FanMode Gpu) GetFanModes()
    {
        var output = Invoke("GetGamingFanBehavior", AcerProtocol.BehaviorMask([Fan.Cpu, Fan.Gpu]));
        return (AcerProtocol.DecodeFanMode(output, Fan.Cpu), AcerProtocol.DecodeFanMode(output, Fan.Gpu));
    }

    public void SetFanModes(IReadOnlyDictionary<Fan, FanMode> modes)
    {
        var input = AcerProtocol.EncodeSetFanBehavior(modes);
        AcerProtocol.EnsureStatusOk(Invoke("SetGamingFanBehavior", input), "SetGamingFanBehavior");
    }

    public int GetFanSpeedPercent(Fan fan) =>
        AcerProtocol.DecodeFanSpeed(Invoke("GetGamingFanSpeed", AcerProtocol.SpeedId(fan)));

    public void SetFanSpeedPercent(Fan fan, int percent)
    {
        var input = AcerProtocol.EncodeSetFanSpeed(fan, percent);
        AcerProtocol.EnsureStatusOk(Invoke("SetGamingFanSpeed", input), "SetGamingFanSpeed");
    }

    public int ReadSensor(Sensor sensor) =>
        AcerProtocol.DecodeSysInfo(Invoke("GetGamingSysInfo", AcerProtocol.EncodeSysInfo(sensor)));

    public OperatingMode GetOperatingMode() =>
        AcerProtocol.DecodeOperatingMode(Invoke("GetGamingMiscSetting", AcerProtocol.MiscOperatingMode));

    public void SetOperatingMode(OperatingMode mode)
    {
        var input = AcerProtocol.EncodeSetOperatingMode(mode);
        AcerProtocol.EnsureStatusOk(Invoke("SetGamingMiscSetting", input), "SetGamingMiscSetting");
    }

    public BatteryHealth GetBatteryHealth()
    {
        using var p = new CimMethodParametersCollection
        {
            CimMethodParameter.Create("uBatteryNo", (byte)1, CimType.UInt8, CimFlags.In),
            CimMethodParameter.Create("uFunctionQuery", (byte)1, CimType.UInt8, CimFlags.In),
            CimMethodParameter.Create("uReserved", new byte[2], CimType.UInt8Array, CimFlags.In),
        };
        return InvokeBattery("GetBatteryHealthControlStatus", p, outs =>
        {
            var ret = outs["uReturn"]?.Value as byte[];
            if (ret is { Length: > 0 } && ret[0] != 0)
            {
                throw new FirmwareException($"GetBatteryHealthControlStatus devolveu uReturn {Convert.ToHexString(ret)}.");
            }

            var list = Convert.ToByte(outs["uFunctionList"]?.Value ?? 0, System.Globalization.CultureInfo.InvariantCulture);
            var status = outs["uFunctionStatus"]?.Value as byte[] ?? [];
            return new BatteryHealth(list, status);
        });
    }

    public void SetBatteryChargeLimit(bool enabled)
    {
        using var p = new CimMethodParametersCollection
        {
            CimMethodParameter.Create("uBatteryNo", (byte)1, CimType.UInt8, CimFlags.In),
            CimMethodParameter.Create("uFunctionMask", ChargeLimitMask, CimType.UInt8, CimFlags.In),
            CimMethodParameter.Create("uFunctionStatus", (byte)(enabled ? 1 : 0), CimType.UInt8, CimFlags.In),
            CimMethodParameter.Create("uReservedIn", new byte[5], CimType.UInt8Array, CimFlags.In),
        };
        InvokeBattery("SetBatteryHealthControl", p, outs =>
        {
            var ret = Convert.ToUInt16(outs["uReturn"]?.Value ?? 0, System.Globalization.CultureInfo.InvariantCulture);
            return ret == 0 ? 0 : throw new FirmwareException($"SetBatteryHealthControl devolveu uReturn 0x{ret:X4}.");
        });
    }

    public KeyboardLighting GetKeyboardLighting()
    {
        var (ledStatus, led) = InvokeForBytes("GetGamingLED", AcerProtocol.LedKeyboardZones);
        var mask = AcerProtocol.DecodeKeyboardZones(ledStatus, led);
        var zones = default(ZoneColors);
        for (var z = 0; z < ZoneColors.Count; z++)
        {
            zones = zones.With(z, AcerProtocol.DecodeZoneColor(Invoke("GetGamingRgbKb", AcerProtocol.ZoneBit(z))));
        }

        var (status, backlight) = InvokeForBytes("GetGamingKBBacklight", AcerProtocol.BacklightKeyboard);
        return AcerProtocol.DecodeKeyboardLighting(status, backlight, zones, mask);
    }

    public void SetKeyboardLighting(KeyboardLighting lighting)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        var backlight = AcerProtocol.EncodeSetKeyboardBacklight(lighting); // valida antes da primeira escrita
        AcerProtocol.EnsureStatusOk(InvokeWithBytes("SetGamingLED", AcerProtocol.EncodeSetKeyboardZones(lighting.ZoneMask)), "SetGamingLED");
        AcerProtocol.EnsureStatusOk(InvokeWithBytes("SetGamingKBBacklight", backlight), "SetGamingKBBacklight");
        if (lighting.Effect != KeyboardEffect.Static)
        {
            return;
        }

        for (var z = 0; z < ZoneColors.Count; z++)
        {
            Thread.Sleep(ZoneColorPause);
            var color = lighting.IsZoneOn(z) ? lighting.Zones[z] : RgbColor.Black; // zona apagada também fica preta
            AcerProtocol.EnsureStatusOk(Invoke("SetGamingRgbKb", AcerProtocol.EncodeSetZoneColor(z, color)), "SetGamingRgbKb");
        }
    }

    /// <summary>Chamada a <see cref="BatteryClass"/>: só métodos da lista; o Set só com a máscara do limite de carga.</summary>
    private T InvokeBattery<T>(string method, CimMethodParametersCollection parameters, Func<CimReadOnlyKeyedCollection<CimMethodParameter>, T> read)
    {
        if (!BatteryAllowlist.Contains(method))
        {
            throw new InvalidOperationException($"Método fora da lista de permissão: {method}.");
        }

        if (method == "SetBatteryHealthControl" && Convert.ToByte(parameters["uFunctionMask"]?.Value, System.Globalization.CultureInfo.InvariantCulture) != ChargeLimitMask)
        {
            throw new InvalidOperationException("SetBatteryHealthControl só é permitido para o limite de carga (máscara 1).");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                _batteryInstance ??= _session.QueryInstances(Namespace, "WQL", $"SELECT * FROM {BatteryClass}", _options).Single();
                using var result = _session.InvokeMethod(Namespace, _batteryInstance, method, parameters, _options);
                if (result.ReturnValue?.Value is false)
                {
                    throw new FirmwareException($"{method} devolveu false.");
                }

                return read(result.OutParameters);
            }
            catch (CimException ex)
            {
                _batteryInstance?.Dispose();
                _batteryInstance = null;
                throw new FirmwareException($"Falha WMI em {method}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>Uma chamada WMI por vez, com timeout. Verifica a lista de permissão antes de tudo.</summary>
    internal ulong Invoke(string method, ulong input)
    {
        var inputType = AllowedInputType(method);
        if (inputType is not (CimType.UInt32 or CimType.UInt64))
        {
            throw new InvalidOperationException($"{method} não recebe número.");
        }

        if (method == "SetGamingMiscSetting" && !AcerProtocol.IsAllowedMiscSetting(input))
        {
            throw new InvalidOperationException($"SetGamingMiscSetting só é permitido para o modo (0x0B); entrada 0x{input:X} recusada.");
        }

        if (method == "SetGamingRgbKb" && !AcerProtocol.IsAllowedSetZoneColor(input))
        {
            throw new InvalidOperationException($"SetGamingRgbKb só é permitido para uma zona do teclado; entrada 0x{input:X} recusada.");
        }

        if (inputType == CimType.UInt32 && input > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(input), $"{method} aceita só UInt32.");
        }

        return InvokeCore(method, inputType == CimType.UInt32 ? (uint)input : input, inputType, outs => ReadUInt64(method, outs));
    }

    /// <summary>Set com entrada <c>UInt8[16]</c> (<c>SetGamingLED</c>, <c>SetGamingKBBacklight</c>), já restrita ao teclado.</summary>
    internal ulong InvokeWithBytes(string method, byte[] input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (AllowedInputType(method) != CimType.UInt8Array)
        {
            throw new InvalidOperationException($"{method} não recebe UInt8[].");
        }

        var allowed = method switch
        {
            "SetGamingLED" => AcerProtocol.IsAllowedSetLed(input),
            "SetGamingKBBacklight" => AcerProtocol.IsAllowedSetBacklight(input),
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidOperationException($"{method}: entrada {Convert.ToHexString(input)} fora do permitido (só a luz do teclado).");
        }

        return InvokeCore(method, input.Clone(), CimType.UInt8Array, outs => ReadUInt64(method, outs));
    }

    /// <summary>Get com saída <c>UInt8[15]</c> + <c>gmReturn</c> (<c>GetGamingLED</c>, <c>GetGamingKBBacklight</c>).</summary>
    internal (byte Status, byte[] Output) InvokeForBytes(string method, uint input)
    {
        if (AllowedInputType(method) != CimType.UInt32 || !method.StartsWith("Get", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{method} não é uma leitura com entrada UInt32.");
        }

        return InvokeCore(method, input, CimType.UInt32, outs =>
        {
            var bytes = outs["gmOutput"]?.Value as byte[] ?? throw new FirmwareException($"{method} não devolveu gmOutput.");
            var status = Convert.ToByte(outs["gmReturn"]?.Value ?? 0, System.Globalization.CultureInfo.InvariantCulture);
            return (status, bytes);
        });
    }

    private static CimType AllowedInputType(string method) =>
        Allowlist.TryGetValue(method, out var type)
            ? type
            : throw new InvalidOperationException($"Método fora da lista de permissão: {method}.");

    private static ulong ReadUInt64(string method, CimReadOnlyKeyedCollection<CimMethodParameter> outs)
    {
        var value = outs["gmOutput"]?.Value ?? throw new FirmwareException($"{method} não devolveu gmOutput.");
        return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A chamada em si: uma por vez, com timeout; erro do WMI descarta a instância em cache.</summary>
    private T InvokeCore<T>(string method, object input, CimType inputType, Func<CimReadOnlyKeyedCollection<CimMethodParameter>, T> read)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                _instance ??= _session.QueryInstances(Namespace, "WQL", $"SELECT * FROM {GamingClass}", _options).Single();
                using var parameters = new CimMethodParametersCollection
                {
                    CimMethodParameter.Create("gmInput", input, inputType, CimFlags.In),
                };
                using var result = _session.InvokeMethod(Namespace, _instance, method, parameters, _options);
                return read(result.OutParameters);
            }
            catch (CimException ex)
            {
                _instance?.Dispose();
                _instance = null;
                throw new FirmwareException($"Falha WMI em {method}: {ex.Message}", ex);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _instance?.Dispose();
            _batteryInstance?.Dispose();
            _options.Dispose();
            _session.Dispose();
        }
    }
}
