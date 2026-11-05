using System.Runtime.InteropServices;

namespace KillerSense.Gpu;

/// <summary>Chamadas diretas a DLLs do Windows/NVIDIA (sem pacotes).</summary>
internal static unsafe partial class NativeMethods
{
    // ---- cfgmgr32: estado de energia do dispositivo (não acorda a GPU) ----
    internal const uint CrSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct DevPropKey
    {
        public Guid FmtId;
        public uint Pid;
    }

    /// <summary>DEVPKEY_Device_PowerData = {a45c254e-df1c-4efd-8020-67d146a850e0}, 32 (CM_POWER_DATA).</summary>
    internal static readonly DevPropKey DevPKeyDevicePowerData = new()
    {
        FmtId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        Pid = 32,
    };

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint CmLocateDevNode(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint CmGetDevNodeProperty(uint devInst, in DevPropKey key, out uint propertyType, byte* buffer, ref uint bufferSize, uint flags);

    // ---- NVML (instalada com o driver NVIDIA em System32) ----
    internal const int NvmlSuccess = 0;
    internal const int NvmlTemperatureGpu = 0;

    [LibraryImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NvmlInit();

    [LibraryImport("nvml.dll", EntryPoint = "nvmlShutdown")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NvmlShutdown();

    [LibraryImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NvmlDeviceGetHandleByIndex(uint index, out nint device);

    [LibraryImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NvmlDeviceGetTemperature(nint device, int sensorType, out uint temperature);

    // ---- NVML: só leitura, para o painel (nvml.h) ----
    internal const int NvmlClockGraphics = 0;
    internal const int NvmlClockMem = 2;
    internal const int NvmlPStateUnknown = 32;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [LibraryImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NvmlDeviceGetClockInfo(nint device, int clockType, out uint clockMhz);

    [LibraryImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NvmlDeviceGetUtilizationRates(nint device, out NvmlUtilization utilization);

    [LibraryImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NvmlDeviceGetPowerUsage(nint device, out uint milliwatts);

    [LibraryImport("nvml.dll", EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NvmlDeviceGetEnforcedPowerLimit(nint device, out uint milliwatts);

    [LibraryImport("nvml.dll", EntryPoint = "nvmlDeviceGetPerformanceState")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int NvmlDeviceGetPerformanceState(nint device, out int pState);
}
