using System.Runtime.InteropServices;
using KillerSense.Core;

namespace KillerSense.App.Platform;

/// <summary>
/// Lista dos processos com o tempo de CPU e a memória privada de cada um, numa chamada só
/// (<c>NtQuerySystemInformation(SystemProcessInformation)</c>, a mesma fonte do Gerenciador de Tarefas).
/// Não abre nenhum processo: funciona sem admin e enxerga também os protegidos.
/// Usar só com a tela aberta; <see cref="Dispose"/> libera o buffer.
/// </summary>
internal sealed unsafe partial class ProcessSnapshot : IDisposable
{
    private const int SystemProcessInformation = 5;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    // Posições em SYSTEM_PROCESS_INFORMATION no x64 (o app só existe em x64).
    private const int OffsetPrivateWorkingSet = 0x08;
    private const int OffsetCreateTime = 0x20;
    private const int OffsetUserTime = 0x28;
    private const int OffsetKernelTime = 0x30;
    private const int OffsetImageNameLength = 0x38;
    private const int OffsetImageNameBuffer = 0x40;
    private const int OffsetProcessId = 0x50;

    private void* _buffer;
    private nuint _size = 512 * 1024;

    public List<ProcessTimes> Read()
    {
        var list = new List<ProcessTimes>(400);
        if (IntPtr.Size != 8)
        {
            return list;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (_buffer is null)
            {
                _buffer = NativeMemory.Alloc(_size);
            }

            var status = NtQuerySystemInformation(SystemProcessInformation, _buffer, (uint)_size, out var needed);
            if (status == StatusInfoLengthMismatch)
            {
                NativeMemory.Free(_buffer);
                _buffer = null;
                _size = Math.Max(_size * 2, (nuint)needed + (64 * 1024)); // a lista pode crescer entre as chamadas
                continue;
            }

            if (status >= 0)
            {
                Parse((byte*)_buffer, list);
            }

            break;
        }

        return list;
    }

    private static void Parse(byte* entry, List<ProcessTimes> list)
    {
        while (true)
        {
            var id = (long)*(nint*)(entry + OffsetProcessId);
            if (id != 0) // 0 = processo ocioso do sistema
            {
                var length = *(ushort*)(entry + OffsetImageNameLength);
                var name = *(char**)(entry + OffsetImageNameBuffer);
                list.Add(new ProcessTimes(
                    id,
                    name is null || length == 0 ? "?" : new string(name, 0, length / 2),
                    *(long*)(entry + OffsetCreateTime),
                    *(long*)(entry + OffsetUserTime) + *(long*)(entry + OffsetKernelTime),
                    *(long*)(entry + OffsetPrivateWorkingSet)));
            }

            var next = *(uint*)entry;
            if (next == 0)
            {
                return;
            }

            entry += next;
        }
    }

    public void Dispose()
    {
        if (_buffer is not null)
        {
            NativeMemory.Free(_buffer);
            _buffer = null;
        }
    }

    [LibraryImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NtQuerySystemInformation(int informationClass, void* buffer, uint length, out uint returnLength);
}
