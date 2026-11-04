using System.Runtime.InteropServices;

namespace KillerSense.Firmware;

/// <summary>Fonte de energia atual (<c>GetSystemPowerStatus</c>). Sem admin, sem firmware.</summary>
public static partial class PowerSource
{
    /// <summary>true = na tomada; false = na bateria; null = o Windows não soube dizer.</summary>
    public static bool? IsOnAc()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return null;
        }

        return status.AcLineStatus switch
        {
            0 => false,
            1 => true,
            _ => null,
        };
    }

    /// <summary>Carga da bateria em % (0–100); null = desconhecida.</summary>
    public static int? BatteryPercent() =>
        GetSystemPowerStatus(out var status) && status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);
}
