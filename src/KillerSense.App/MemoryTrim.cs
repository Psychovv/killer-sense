using System.Runtime;
using System.Runtime.InteropServices;

namespace KillerSense.App;

/// <summary>
/// Quando a janela é destruída e o app fica só na bandeja: coleta o heap compactando e devolve ao
/// Windows as páginas que a janela usava, para o modo bandeja ficar leve.
/// </summary>
internal static partial class MemoryTrim
{
    public static void AfterWindowClosed()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        _ = SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1); // falha aqui não importa
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(nint process, nint minimumWorkingSetSize, nint maximumWorkingSetSize);
}
