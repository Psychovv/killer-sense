namespace KillerSense.Core;

/// <summary>Offset de clock da GPU (MHz) no núcleo e na memória, somado a todos os P-states pelo driver.</summary>
public readonly record struct GpuClockOffset(int CoreMhz, int MemoryMhz)
{
    public static GpuClockOffset Zero => default;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsZero => CoreMhz == 0 && MemoryMhz == 0;

    public override string ToString() => $"núcleo {CoreMhz:+0;-0;0} MHz, memória {MemoryMhz:+0;-0;0} MHz";
}

/// <summary>Faixa que o driver aceita para um offset (MHz).</summary>
public readonly record struct GpuOffsetRange(int MinMhz, int MaxMhz);

/// <summary>Offset aplicado agora no driver e as faixas que ele aceita (null se o driver não informar).</summary>
public sealed record GpuOffsetReading(GpuClockOffset Current, GpuOffsetRange? CoreRange, GpuOffsetRange? MemoryRange);

/// <summary>Quem lê e muda o offset de clock da GPU (NVAPI no hardware, simulado nos testes).</summary>
public interface IGpuClockOffsets
{
    /// <summary>Lê o offset atual. Null com <paramref name="failure"/> preenchido se o driver não respondeu.</summary>
    GpuOffsetReading? Read(out string? failure);

    /// <summary>Aplica o offset (já limitado pela <see cref="GpuOffsetPolicy"/>). False com o erro do driver.</summary>
    bool Apply(GpuClockOffset offset, out string? failure);
}

/// <summary>
/// Limites do killer-sense para o offset, mais apertados que os do driver. O padrão da Acer para o
/// PHN16-72 é +100 núcleo / +200 memória só no Turbo; o teto deixa uma folga pequena acima disso.
/// Valores negativos (subclock) são seguros e têm faixa maior.
/// </summary>
public static class GpuOffsetPolicy
{
    public const int CoreMinMhz = -300;
    public const int CoreMaxMhz = 150;
    public const int MemoryMinMhz = -500;
    public const int MemoryMaxMhz = 300;

    /// <summary>Limita aos tetos do killer-sense e, se conhecida, à faixa do driver.</summary>
    public static GpuClockOffset Clamp(GpuClockOffset wanted, GpuOffsetReading? driver = null)
    {
        var core = Math.Clamp(wanted.CoreMhz, CoreMinMhz, CoreMaxMhz);
        var memory = Math.Clamp(wanted.MemoryMhz, MemoryMinMhz, MemoryMaxMhz);
        if (driver?.CoreRange is { } cr)
        {
            core = Math.Clamp(core, Math.Min(cr.MinMhz, 0), Math.Max(cr.MaxMhz, 0));
        }

        if (driver?.MemoryRange is { } mr)
        {
            memory = Math.Clamp(memory, Math.Min(mr.MinMhz, 0), Math.Max(mr.MaxMhz, 0));
        }

        return new GpuClockOffset(core, memory);
    }
}

/// <summary>
/// Offsets por modo e se o killer-sense deve aplicá-los. Padrão: DESLIGADO, com a tabela da Acer
/// (só o Turbo acelera). Eco nunca tem offset.
/// </summary>
public sealed record GpuOffsetSettings(bool Enabled, IReadOnlyDictionary<OperatingMode, GpuClockOffset> PerMode)
{
    /// <summary>Modos que têm offset configurável (Eco fica sempre em 0, como no padrão da Acer).</summary>
    public static IReadOnlyList<OperatingMode> ConfigurableModes { get; } =
        [OperatingMode.Quiet, OperatingMode.Balanced, OperatingMode.Performance, OperatingMode.Turbo];

    /// <summary>Tabela do <c>.ini</c> da Acer para o PHN16-72.</summary>
    public static IReadOnlyDictionary<OperatingMode, GpuClockOffset> AcerDefaults { get; } = new Dictionary<OperatingMode, GpuClockOffset>
    {
        [OperatingMode.Quiet] = GpuClockOffset.Zero,
        [OperatingMode.Balanced] = GpuClockOffset.Zero,
        [OperatingMode.Performance] = GpuClockOffset.Zero,
        [OperatingMode.Turbo] = new(100, 200),
    };

    public static GpuOffsetSettings Default { get; } = new(false, AcerDefaults);

    /// <summary>Offset do modo, já limitado; Eco e modos sem valor ⇒ zero.</summary>
    public GpuClockOffset For(OperatingMode mode) =>
        mode != OperatingMode.Eco && PerMode.TryGetValue(mode, out var o) ? GpuOffsetPolicy.Clamp(o) : GpuClockOffset.Zero;

    /// <summary>Null se tudo estiver dentro dos limites; senão, o motivo.</summary>
    public string? Validate()
    {
        foreach (var (mode, o) in PerMode)
        {
            if (!Enum.IsDefined(mode))
            {
                return $"Modo desconhecido: {(int)mode}.";
            }

            if (mode == OperatingMode.Eco && !o.IsZero)
            {
                return "O modo Eco não aceita offset.";
            }

            if (o.CoreMhz is < GpuOffsetPolicy.CoreMinMhz or > GpuOffsetPolicy.CoreMaxMhz)
            {
                return $"Núcleo fora da faixa ({GpuOffsetPolicy.CoreMinMhz} a +{GpuOffsetPolicy.CoreMaxMhz} MHz): {o.CoreMhz}.";
            }

            if (o.MemoryMhz is < GpuOffsetPolicy.MemoryMinMhz or > GpuOffsetPolicy.MemoryMaxMhz)
            {
                return $"Memória fora da faixa ({GpuOffsetPolicy.MemoryMinMhz} a +{GpuOffsetPolicy.MemoryMaxMhz} MHz): {o.MemoryMhz}.";
            }
        }

        return null;
    }
}

/// <summary>Estado do offset para a interface.</summary>
/// <param name="Enabled">O usuário ligou a aplicação por modo.</param>
/// <param name="Current">Offset que o driver tem segundo a última leitura (null = ainda não lido).</param>
/// <param name="Target">O que o killer-sense quer no modo atual (null = não mexe).</param>
/// <param name="Pending">Há algo a aplicar esperando a GPU ligar.</param>
/// <param name="Hot">Offset retirado por temperatura alta ou leitura inválida.</param>
/// <param name="Message">Último erro do driver, se houver.</param>
public sealed record GpuOffsetStatus(bool Enabled, GpuClockOffset? Current, GpuClockOffset? Target, bool Pending, bool Hot, string? Message);

/// <summary>GPU simulada para o offset (modo --simulated e testes).</summary>
public sealed class SimulatedGpuClockOffsets : IGpuClockOffsets
{
    public GpuClockOffset Current { get; set; }

    public int Writes { get; private set; }

    public int Reads { get; private set; }

    /// <summary>Se preenchido, toda escrita falha com esta mensagem.</summary>
    public string? FailWith { get; set; }

    /// <summary>Se verdadeiro, a escrita "dá certo" mas o driver não muda o valor.</summary>
    public bool IgnoreWrites { get; set; }

    public GpuOffsetReading? Read(out string? failure)
    {
        Reads++;
        failure = null;
        return new GpuOffsetReading(Current, new GpuOffsetRange(-1000, 1000), new GpuOffsetRange(-1000, 3000));
    }

    public bool Apply(GpuClockOffset offset, out string? failure)
    {
        Writes++;
        failure = FailWith;
        if (failure is not null)
        {
            return false;
        }

        if (!IgnoreWrites)
        {
            Current = offset;
        }

        return true;
    }
}
