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
    };

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
        if (!Allowlist.TryGetValue(method, out var inputType))
        {
            throw new InvalidOperationException($"Método fora da lista de permissão: {method}.");
        }

        if (method == "SetGamingMiscSetting" && !AcerProtocol.IsAllowedMiscSetting(input))
        {
            throw new InvalidOperationException($"SetGamingMiscSetting só é permitido para o modo (0x0B); entrada 0x{input:X} recusada.");
        }

        if (inputType == CimType.UInt32 && input > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(input), $"{method} aceita só UInt32.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                _instance ??= _session.QueryInstances(Namespace, "WQL", $"SELECT * FROM {GamingClass}", _options).Single();
                using var parameters = new CimMethodParametersCollection
                {
                    CimMethodParameter.Create(
                        "gmInput",
                        inputType == CimType.UInt32 ? (object)(uint)input : input,
                        inputType,
                        CimFlags.In),
                };
                using var result = _session.InvokeMethod(Namespace, _instance, method, parameters, _options);
                var value = result.OutParameters["gmOutput"]?.Value
                    ?? throw new FirmwareException($"{method} não devolveu gmOutput.");
                return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
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
