namespace KillerSense.Core;

/// <summary>
/// Um processo numa leitura: id, nome da imagem, momento de criação (separa um id reaproveitado), tempo de CPU
/// somado (usuário + kernel, em unidades de 100 ns) e memória privada em uso (bytes).
/// </summary>
public readonly record struct ProcessTimes(long Id, string Name, long CreateTime, long CpuTime, long PrivateBytes);

/// <summary>Um programa (os processos com o mesmo nome somados) e quanto ele usa agora.</summary>
public sealed record ProgramUsage(string Name, int Processes, double CpuPercent, long PrivateBytes);

/// <summary>
/// Programas que mais usam a CPU, comparando duas leituras do tempo de CPU de cada processo. A porcentagem é
/// de todos os núcleos juntos, como na aba Processos do Gerenciador de Tarefas. Processos com o mesmo nome
/// (chrome, msedge…) viram uma linha só.
/// </summary>
public sealed class ProcessCpuTracker
{
    private Dictionary<long, (long Create, long Cpu)> _last = [];
    private bool _primed;

    /// <summary>
    /// Registra a leitura e devolve os <paramref name="top"/> que mais usaram a CPU desde a anterior.
    /// A primeira leitura só serve de base (lista vazia).
    /// </summary>
    /// <param name="elapsed">Tempo de relógio desde a leitura anterior, em unidades de 100 ns.</param>
    public IReadOnlyList<ProgramUsage> Update(IEnumerable<ProcessTimes> processes, long elapsed, int processorCount, int top)
    {
        ArgumentNullException.ThrowIfNull(processes);
        var next = new Dictionary<long, (long Create, long Cpu)>(_last.Count == 0 ? 256 : _last.Count);
        var byName = new Dictionary<string, (string Name, int Count, long Cpu, long Private)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in processes)
        {
            next[p.Id] = (p.CreateTime, p.CpuTime);
            // Processo novo (ou id reaproveitado): ainda sem base, conta a partir da próxima leitura.
            var used = _last.TryGetValue(p.Id, out var prev) && prev.Create == p.CreateTime ? Math.Max(0, p.CpuTime - prev.Cpu) : 0;
            var name = DisplayName(p.Name);
            byName[name] = byName.TryGetValue(name, out var sum)
                ? (sum.Name, sum.Count + 1, sum.Cpu + used, sum.Private + p.PrivateBytes)
                : (name, 1, used, p.PrivateBytes);
        }

        var primed = _primed;
        _last = next;
        _primed = true;
        if (!primed || elapsed <= 0 || processorCount <= 0 || top <= 0)
        {
            return [];
        }

        var capacity = (double)elapsed * processorCount;
        return [.. byName.Values
            .Select(v => new ProgramUsage(v.Name, v.Count, Math.Min(100, v.Cpu * 100 / capacity), v.Private))
            .OrderByDescending(u => u.CpuPercent)
            .ThenByDescending(u => u.PrivateBytes)
            .Take(top)];
    }

    /// <summary>Nome para a tela: sem o ".exe".</summary>
    public static string DisplayName(string image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && image.Length > 4 ? image[..^4] : image;
    }
}
