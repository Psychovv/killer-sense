using KillerSense.Core;
using Microsoft.Management.Infrastructure;

namespace KillerSense.Gpu;

/// <summary>
/// Temperatura da GPU NVIDIA sem acordá-la: primeiro lê o estado de energia do dispositivo no Windows
/// (D0 = ligada); só então consulta o NVML. Com a GPU desligada (Optimus) devolve <see cref="GpuReading.Off"/>.
/// </summary>
public sealed class NvidiaGpuSensor : IGpuSensor, IDisposable
{
    private const int PowerStateD0 = 1; // DEVICE_POWER_STATE: PowerDeviceD0
    private readonly Lock _gate = new();
    private readonly string? _instanceId;
    private bool _nvmlReady;
    private nint _device;
    private bool _disposed;

    public NvidiaGpuSensor()
    {
        _instanceId = FindNvidiaDisplayInstanceId();
    }

    /// <summary>Id PnP da GPU NVIDIA encontrada (null se não houver).</summary>
    public string? InstanceId => _instanceId;

    public GpuReading Read()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_instanceId is null)
            {
                return GpuReading.Error;
            }

            var state = ReadPowerState(_instanceId);
            if (state is null)
            {
                return GpuReading.Error;
            }

            if (state != PowerStateD0)
            {
                // Desligada: não chamar o NVML (isso a acordaria) e fechar a sessão para não segurá-la acordada.
                if (_nvmlReady)
                {
                    _ = NativeMethods.NvmlShutdown();
                    _nvmlReady = false;
                }

                return GpuReading.Off;
            }

            try
            {
                if (!_nvmlReady)
                {
                    if (NativeMethods.NvmlInit() != NativeMethods.NvmlSuccess
                        || NativeMethods.NvmlDeviceGetHandleByIndex(0, out _device) != NativeMethods.NvmlSuccess)
                    {
                        return GpuReading.Error;
                    }

                    _nvmlReady = true;
                }

                return NativeMethods.NvmlDeviceGetTemperature(_device, NativeMethods.NvmlTemperatureGpu, out var t) == NativeMethods.NvmlSuccess
                    ? GpuReading.Value(t, ReadTelemetry(_device))
                    : GpuReading.Error;
            }
            catch (DllNotFoundException)
            {
                return GpuReading.Error;
            }
            catch (EntryPointNotFoundException)
            {
                return GpuReading.Error;
            }
        }
    }

    /// <summary>
    /// Clocks, uso e potência para o painel. Só é chamado com a GPU ligada (D0) e a sessão do NVML já
    /// aberta para a temperatura; cada valor que o driver não informa fica null.
    /// </summary>
    private static GpuTelemetry ReadTelemetry(nint device)
    {
        const int ok = NativeMethods.NvmlSuccess;
        int? core = NativeMethods.NvmlDeviceGetClockInfo(device, NativeMethods.NvmlClockGraphics, out var c) == ok ? (int)c : null;
        int? mem = NativeMethods.NvmlDeviceGetClockInfo(device, NativeMethods.NvmlClockMem, out var m) == ok ? (int)m : null;
        int? util = NativeMethods.NvmlDeviceGetUtilizationRates(device, out var u) == ok ? (int)Math.Min(u.Gpu, 100) : null;
        double? power = NativeMethods.NvmlDeviceGetPowerUsage(device, out var p) == ok ? p / 1000.0 : null;
        double? limit = NativeMethods.NvmlDeviceGetEnforcedPowerLimit(device, out var l) == ok ? l / 1000.0 : null;
        int? pState = NativeMethods.NvmlDeviceGetPerformanceState(device, out var s) == ok && s is >= 0 and < NativeMethods.NvmlPStateUnknown ? s : null;
        return new GpuTelemetry(core, mem, util, power, limit, pState);
    }

    /// <summary>Estado de energia mais recente (1 = D0 … 4 = D3), ou null se não deu para ler.</summary>
    internal static unsafe int? ReadPowerState(string instanceId)
    {
        if (NativeMethods.CmLocateDevNode(out var devInst, instanceId, 0) != NativeMethods.CrSuccess)
        {
            return null;
        }

        var buffer = stackalloc byte[64];
        uint size = 64;
        var key = NativeMethods.DevPKeyDevicePowerData;
        if (NativeMethods.CmGetDevNodeProperty(devInst, in key, out _, buffer, ref size, 0) != NativeMethods.CrSuccess || size < 8)
        {
            return null;
        }

        return *(int*)(buffer + 4); // CM_POWER_DATA.PD_MostRecentPowerState
    }

    private static string? FindNvidiaDisplayInstanceId()
    {
        try
        {
            using var session = CimSession.Create(null);
            foreach (var vc in session.QueryInstances(@"root\cimv2", "WQL", "SELECT PNPDeviceID FROM Win32_VideoController"))
            {
                using (vc)
                {
                    if (vc.CimInstanceProperties["PNPDeviceID"]?.Value is string id
                        && id.StartsWith(@"PCI\VEN_10DE&", StringComparison.OrdinalIgnoreCase))
                    {
                        return id;
                    }
                }
            }
        }
        catch (CimException)
        {
            // sem WMI: sem GPU
        }

        return null;
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
            if (_nvmlReady)
            {
                _ = NativeMethods.NvmlShutdown();
            }
        }
    }
}
