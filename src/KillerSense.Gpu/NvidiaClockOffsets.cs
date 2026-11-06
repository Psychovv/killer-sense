using KillerSense.Core;

namespace KillerSense.Gpu;

/// <summary>
/// Offset de clock da GPU NVIDIA pela NVAPI, no P0 (o driver soma o mesmo delta nos outros estados).
/// A leitura usa <c>GetPstates20</c>; a escrita, <c>SetPstates20</c>, que exige administrador (o serviço
/// roda como SYSTEM). O offset vale até reiniciar o PC ou o driver; gravar 0 desfaz.
/// </summary>
public sealed class NvidiaClockOffsets : IGpuClockOffsets
{
    private readonly Lock _gate = new();
    private nint _gpu;

    public GpuOffsetReading? Read(out string? failure)
    {
        lock (_gate)
        {
            if (!Open(out failure))
            {
                return null;
            }

            var info = new byte[NvApi.InfoSize];
            var status = NvApi.GetPstates20(_gpu, info);
            if (status != NvApi.Ok)
            {
                failure = $"GetPstates20 falhou ({status})";
                _gpu = 0; // o handle pode ter ficado inválido (driver reiniciado)
                return null;
            }

            var p0 = Pstates20Layout.Clocks(info).Where(c => c.PstateId == 0).ToList();
            var core = p0.FirstOrDefault(c => c.Domain == NvApi.DomainGraphics);
            var memory = p0.FirstOrDefault(c => c.Domain == NvApi.DomainMemory);
            failure = null;
            return new GpuOffsetReading(
                new GpuClockOffset(core.DeltaKhz / 1000, memory.DeltaKhz / 1000),
                RangeOf(core),
                RangeOf(memory));
        }
    }

    public bool Apply(GpuClockOffset offset, out string? failure)
    {
        lock (_gate)
        {
            if (!Open(out failure))
            {
                return false;
            }

            var status = NvApi.SetPstates20(_gpu, Pstates20Layout.BuildSet(offset.CoreMhz * 1000, offset.MemoryMhz * 1000));
            if (status != NvApi.Ok)
            {
                failure = status == -137 // NVAPI_INVALID_USER_PRIVILEGE
                    ? "o driver recusou: precisa de administrador"
                    : $"SetPstates20 falhou ({status})";
                return false;
            }

            failure = null;
            return true;
        }
    }

    /// <summary>Texto com todas as entradas de clock de todos os P-states (para a ferramenta de testes).</summary>
    public IReadOnlyList<string> Dump()
    {
        lock (_gate)
        {
            if (!Open(out var error))
            {
                return [$"NVAPI indisponível: {error}"];
            }

            var lines = new List<string> { $"GPU: {NvApi.FullName(_gpu) ?? "?"}" };
            var info = new byte[NvApi.InfoSize];
            var status = NvApi.GetPstates20(_gpu, info);
            lines.Add($"GetPstates20 (versão 0x{NvApi.InfoVersion2:X}): {status}");
            if (status != NvApi.Ok)
            {
                return lines;
            }

            var u32 = (int o) => BitConverter.ToUInt32(info, o);
            lines.Add($"editável={u32(4) & 1}  P-states={u32(8)}  clocks={u32(12)}  tensões base={u32(16)}");
            foreach (var c in Pstates20Layout.Clocks(info))
            {
                var domain = c.Domain switch
                {
                    NvApi.DomainGraphics => "núcleo",
                    NvApi.DomainMemory => "memória",
                    _ => $"domínio {c.Domain}",
                };
                lines.Add($"P{c.PstateId}  {domain,-10} {c.FreqKhz / 1000,5}..{c.MaxFreqKhz / 1000,5} MHz  " +
                          $"delta {c.DeltaKhz / 1000,5} MHz (faixa {c.DeltaMinKhz / 1000}..{c.DeltaMaxKhz / 1000})  editável={c.Editable}");
            }

            return lines;
        }
    }

    private static GpuOffsetRange? RangeOf(Pstate20Clock clock) =>
        clock.Editable ? new GpuOffsetRange(clock.DeltaMinKhz / 1000, clock.DeltaMaxKhz / 1000) : null;

    private bool Open(out string? error)
    {
        error = null;
        if (_gpu != 0)
        {
            return true;
        }

        var status = NvApi.Initialize();
        if (status != NvApi.Ok)
        {
            error = status == -1 ? "nvapi64.dll não encontrada" : $"NvAPI_Initialize falhou ({status})";
            return false;
        }

        status = NvApi.FirstGpu(out _gpu);
        if (status != NvApi.Ok)
        {
            error = $"nenhuma GPU NVIDIA ({status})";
            _gpu = 0;
            return false;
        }

        return true;
    }
}
