using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KillerSense.App.Platform;

/// <summary>Leitura da bateria pelo driver do Windows (sem admin, sem firmware da Acer).</summary>
/// <param name="Percent">Carga, 0–100.</param>
/// <param name="FullChargedMwh">Capacidade atual (carga completa), mWh.</param>
/// <param name="DesignMwh">Capacidade de fábrica, mWh.</param>
/// <param name="CycleCount">Ciclos; null quando o firmware não informa (0).</param>
/// <param name="RateMw">Potência: positiva carregando, negativa descarregando; null = desconhecida.</param>
public sealed record BatteryReading(double Percent, bool OnLine, bool Charging, bool Discharging, double? FullChargedMwh, double? DesignMwh, int? CycleCount, double? RateMw)
{
    /// <summary>Saúde = capacidade atual / de fábrica.</summary>
    public double? HealthPercent => FullChargedMwh is { } f && DesignMwh is { } d && d > 0 ? Math.Min(100, f / d * 100) : null;
}

/// <summary>
/// <c>IOCTL_BATTERY_QUERY_INFORMATION</c> / <c>IOCTL_BATTERY_QUERY_STATUS</c> na primeira bateria
/// (classe de dispositivo GUID_DEVCLASS_BATTERY). Qualquer falha devolve null.
/// </summary>
internal static unsafe partial class WindowsBattery
{
    private static readonly Guid BatteryClass = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
    private const uint DigcfPresent = 0x2;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint IoctlQueryTag = 0x294040;
    private const uint IoctlQueryInformation = 0x294044;
    private const uint IoctlQueryStatus = 0x29404C;
    private const uint PowerOnLine = 0x1;
    private const uint PowerDischarging = 0x2;
    private const uint PowerCharging = 0x4;
    private const uint CapacityRelative = 0x40000000;
    private const uint UnknownValue = 0xFFFFFFFF;

    public static BatteryReading? Read()
    {
        var path = FirstBatteryPath();
        if (path is null)
        {
            return null;
        }

        using var handle = CreateFileW(path, 0xC0000000 /* GENERIC_READ|WRITE */, 3 /* FILE_SHARE_READ|WRITE */, 0, 3 /* OPEN_EXISTING */, 0x80, 0);
        if (handle.IsInvalid)
        {
            return null;
        }

        uint wait = 0, tag = 0;
        if (!DeviceIoControl(handle, IoctlQueryTag, &wait, sizeof(uint), &tag, sizeof(uint), out _, 0) || tag == 0)
        {
            return null;
        }

        var query = new BatteryQueryInformation { BatteryTag = tag, InformationLevel = 0 /* BatteryInformation */ };
        BatteryInformation info;
        if (!DeviceIoControl(handle, IoctlQueryInformation, &query, sizeof(BatteryQueryInformation), &info, sizeof(BatteryInformation), out _, 0))
        {
            return null;
        }

        var waitStatus = new BatteryWaitStatus { BatteryTag = tag };
        BatteryStatus status;
        if (!DeviceIoControl(handle, IoctlQueryStatus, &waitStatus, sizeof(BatteryWaitStatus), &status, sizeof(BatteryStatus), out _, 0))
        {
            return null;
        }

        var relative = (info.Capabilities & CapacityRelative) != 0;
        double? full = relative || info.FullChargedCapacity is 0 or UnknownValue ? null : info.FullChargedCapacity;
        double? design = relative || info.DesignedCapacity is 0 or UnknownValue ? null : info.DesignedCapacity;
        var percent = status.Capacity == UnknownValue ? 0
            : relative ? status.Capacity
            : full is { } f ? Math.Clamp(status.Capacity / f * 100, 0, 100) : 0;
        double? rate = status.Rate == unchecked((int)0x80000000) || relative ? null : status.Rate;
        return new BatteryReading(
            percent,
            (status.PowerState & PowerOnLine) != 0,
            (status.PowerState & PowerCharging) != 0,
            (status.PowerState & PowerDischarging) != 0,
            full,
            design,
            info.CycleCount == 0 ? null : (int)info.CycleCount,
            rate);
    }

    private static string? FirstBatteryPath()
    {
        var guid = BatteryClass;
        var set = SetupDiGetClassDevsW(&guid, 0, 0, DigcfPresent | DigcfDeviceInterface);
        if (set == -1)
        {
            return null;
        }

        try
        {
            var data = new SpDeviceInterfaceData { CbSize = sizeof(SpDeviceInterfaceData) };
            if (!SetupDiEnumDeviceInterfaces(set, 0, &guid, 0, &data))
            {
                return null;
            }

            const int size = 1024;
            var buffer = stackalloc byte[size];
            *(int*)buffer = IntPtr.Size == 8 ? 8 : 6; // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
            if (!SetupDiGetDeviceInterfaceDetailW(set, &data, buffer, size, out _, 0))
            {
                return null;
            }

            return new string((char*)(buffer + 4));
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryQueryInformation
    {
        public uint BatteryTag;
        public int InformationLevel;
        public uint AtRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public uint Capabilities;
        public byte Technology;
        public byte Reserved1;
        public byte Reserved2;
        public byte Reserved3;
        public uint Chemistry;
        public uint DesignedCapacity;
        public uint FullChargedCapacity;
        public uint DefaultAlert1;
        public uint DefaultAlert2;
        public uint CriticalBias;
        public uint CycleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryWaitStatus
    {
        public uint BatteryTag;
        public uint Timeout;
        public uint PowerState;
        public uint LowCapacity;
        public uint HighCapacity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryStatus
    {
        public uint PowerState;
        public uint Capacity;
        public uint Voltage;
        public int Rate;
    }

    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static partial nint SetupDiGetClassDevsW(Guid* classGuid, nint enumerator, nint hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, Guid* interfaceClassGuid, uint memberIndex, SpDeviceInterfaceData* deviceInterfaceData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(nint deviceInfoSet, SpDeviceInterfaceData* deviceInterfaceData, byte* detail, int detailSize, out int requiredSize, nint deviceInfoData);

    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* inBuffer, int inSize, void* outBuffer, int outSize, out int returned, nint overlapped);
}
