using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KillerSense.Gpu;

/// <summary>
/// Acesso mínimo à NVAPI (<c>nvapi64.dll</c>, instalada com o driver): ler e mudar o offset de clock
/// do estado P0. A DLL só exporta <c>nvapi_QueryInterface</c>; cada função é pedida pelo seu id.
/// <para>
/// <c>GetPstates20</c> é do SDK público. <c>SetPstates20</c> não é (é a mesma chamada que o serviço da
/// Acer usa e que ferramentas de overclock usam); o formato da estrutura é o mesmo da leitura.
/// </para>
/// </summary>
internal static unsafe partial class NvApi
{
    internal const int Ok = 0;

    // Ids de nvapi_interface.h (públicos), menos SetPstates20.
    private const uint IdInitialize = 0x0150E828;
    private const uint IdEnumPhysicalGpus = 0xE5AC921F;
    private const uint IdGetFullName = 0xCEEE8E9F;
    private const uint IdGetPstates20 = 0x6FF81213;
    private const uint IdSetPstates20 = 0x0F4DAE6B;

    /// <summary>
    /// NV_GPU_PERF_PSTATES20_INFO_V2: cabeçalho de 20 bytes, 16 P-states de 456 bytes e o bloco de
    /// sobretensão (100 bytes) = 7416 bytes. Versão = tamanho | (n &lt;&lt; 16).
    /// </summary>
    internal const int InfoSize = 0x1CF8;
    internal const uint InfoVersion2 = InfoSize | (2u << 16);
    internal const uint InfoVersion1 = 0x1C94 | (1u << 16); // sem o bloco de sobretensão (usado na escrita)

    internal const int HeaderSize = 20;
    internal const int PstateSize = 456;
    internal const int ClockEntrySize = 44;
    internal const int MaxPstates = 16;
    internal const int MaxClocks = 8;

    internal const int DomainGraphics = 0; // NVAPI_GPU_PUBLIC_CLOCK_GRAPHICS
    internal const int DomainMemory = 4;   // NVAPI_GPU_PUBLIC_CLOCK_MEMORY

    private static readonly Lock Gate = new();
    private static bool _loaded;
    private static delegate* unmanaged[Cdecl]<int> _initialize;
    private static delegate* unmanaged[Cdecl]<nint*, uint*, int> _enumPhysicalGpus;
    private static delegate* unmanaged[Cdecl]<nint, byte*, int> _getFullName;
    private static delegate* unmanaged[Cdecl]<nint, byte*, int> _getPstates20;
    private static delegate* unmanaged[Cdecl]<nint, byte*, int> _setPstates20;

    [LibraryImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint QueryInterface(uint id);

    /// <summary>Carrega a DLL e inicializa. Devolve o código NVAPI (0 = ok) ou -1 se a DLL não existe.</summary>
    internal static int Initialize()
    {
        lock (Gate)
        {
            if (!_loaded)
            {
                try
                {
                    _initialize = (delegate* unmanaged[Cdecl]<int>)QueryInterface(IdInitialize);
                    _enumPhysicalGpus = (delegate* unmanaged[Cdecl]<nint*, uint*, int>)QueryInterface(IdEnumPhysicalGpus);
                    _getFullName = (delegate* unmanaged[Cdecl]<nint, byte*, int>)QueryInterface(IdGetFullName);
                    _getPstates20 = (delegate* unmanaged[Cdecl]<nint, byte*, int>)QueryInterface(IdGetPstates20);
                    _setPstates20 = (delegate* unmanaged[Cdecl]<nint, byte*, int>)QueryInterface(IdSetPstates20);
                }
                catch (DllNotFoundException)
                {
                    return -1;
                }
                catch (EntryPointNotFoundException)
                {
                    return -1;
                }

                if (_initialize == null || _enumPhysicalGpus == null || _getPstates20 == null)
                {
                    return -1;
                }

                _loaded = true;
            }

            return _initialize();
        }
    }

    /// <summary>Primeira GPU física (num notebook com Optimus só a NVIDIA aparece).</summary>
    internal static int FirstGpu(out nint gpu)
    {
        var handles = stackalloc nint[64];
        uint count = 0;
        var status = _enumPhysicalGpus(handles, &count);
        gpu = status == Ok && count > 0 ? handles[0] : 0;
        return status == Ok && count == 0 ? -6 /* NVAPI_NVIDIA_DEVICE_NOT_FOUND */ : status;
    }

    internal static string? FullName(nint gpu)
    {
        if (_getFullName == null)
        {
            return null;
        }

        var name = stackalloc byte[64];
        return _getFullName(gpu, name) == Ok ? Marshal.PtrToStringAnsi((nint)name) : null;
    }

    internal static int GetPstates20(nint gpu, Span<byte> info)
    {
        info.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(info, InfoVersion2);
        fixed (byte* p = info)
        {
            return _getPstates20(gpu, p);
        }
    }

    internal static int SetPstates20(nint gpu, ReadOnlySpan<byte> info)
    {
        if (_setPstates20 == null)
        {
            return -3; // NVAPI_NO_IMPLEMENTATION
        }

        fixed (byte* p = info)
        {
            return _setPstates20(gpu, p);
        }
    }
}

/// <summary>Uma entrada de clock do P-state lida da estrutura.</summary>
internal readonly record struct Pstate20Clock(
    int PstateId, int Domain, bool Editable, int DeltaKhz, int DeltaMinKhz, int DeltaMaxKhz, uint FreqKhz, uint MaxFreqKhz);

/// <summary>Leitura e montagem do buffer de <c>NV_GPU_PERF_PSTATES20_INFO</c>.</summary>
internal static class Pstates20Layout
{
    internal static IEnumerable<Pstate20Clock> Clocks(byte[] info)
    {
        var numPstates = Math.Min(U32(info, 8), (uint)NvApi.MaxPstates);
        var numClocks = Math.Min(U32(info, 12), (uint)NvApi.MaxClocks);
        for (var i = 0; i < numPstates; i++)
        {
            var ps = NvApi.HeaderSize + (i * NvApi.PstateSize);
            var pstateId = (int)U32(info, ps);
            for (var j = 0; j < numClocks; j++)
            {
                var c = ps + 8 + (j * NvApi.ClockEntrySize);
                var type = U32(info, c + 4);
                var freq = U32(info, c + 24);
                var max = type == 1 ? U32(info, c + 28) : freq; // tipo 1 = faixa (min, max)
                yield return new Pstate20Clock(
                    pstateId,
                    (int)U32(info, c),
                    (U32(info, c + 8) & 1) != 0,
                    I32(info, c + 12),
                    I32(info, c + 16),
                    I32(info, c + 20),
                    freq,
                    max);
            }
        }
    }

    /// <summary>
    /// Buffer de escrita (versão 1): só o P0 e os dois domínios (núcleo e memória) com os deltas pedidos,
    /// no mesmo formato que ferramentas de overclock usam.
    /// </summary>
    internal static byte[] BuildSet(int coreDeltaKhz, int memoryDeltaKhz)
    {
        var info = new byte[NvApi.InfoSize];
        BinaryPrimitives.WriteUInt32LittleEndian(info, NvApi.InfoVersion1);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(8), 1);  // numPstates
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(12), 2); // numClocks
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(16), 0); // numBaseVoltages
        var ps = NvApi.HeaderSize; // pstateId = 0 (P0)
        WriteClock(info, ps + 8, NvApi.DomainGraphics, coreDeltaKhz);
        WriteClock(info, ps + 8 + NvApi.ClockEntrySize, NvApi.DomainMemory, memoryDeltaKhz);
        return info;
    }

    private static void WriteClock(byte[] info, int offset, int domain, int deltaKhz)
    {
        BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(offset), domain);
        BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(offset + 12), deltaKhz);
    }

    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));

    private static int I32(byte[] b, int o) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o));
}
