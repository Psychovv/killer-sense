using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KillerSense.App.Platform;

/// <summary>Memória RAM do Windows (<c>GlobalMemoryStatusEx</c>): total utilizável e quanto está livre.</summary>
internal static partial class MemoryStatus
{
    /// <summary>(total, disponível) em bytes; null se o Windows não responder.</summary>
    public static (ulong Total, ulong Available)? Read()
    {
        var status = new MemoryStatusEx { Length = (uint)Unsafe.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0 ? (status.TotalPhys, status.AvailPhys) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
