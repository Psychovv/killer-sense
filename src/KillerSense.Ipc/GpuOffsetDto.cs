using KillerSense.Core;

namespace KillerSense.Ipc;

/// <summary>Offset de um modo (MHz).</summary>
public sealed record GpuModeOffsetDto(OperatingMode Mode, int CoreMhz, int MemoryMhz);

/// <summary>
/// Configuração do offset da GPU serializável (pipe e <c>gpu-offsets.json</c>). Sempre validada por
/// <see cref="TryToSettings"/> no serviço.
/// </summary>
public sealed record GpuOffsetSettingsDto(bool Enabled, IReadOnlyList<GpuModeOffsetDto> Modes)
{
    public static GpuOffsetSettingsDto From(GpuOffsetSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.Enabled, [.. s.PerMode.OrderBy(p => p.Key).Select(p => new GpuModeOffsetDto(p.Key, p.Value.CoreMhz, p.Value.MemoryMhz))]);
    }

    public GpuOffsetSettings? TryToSettings(out string? error)
    {
        if (Modes is null || Modes.Count > 8)
        {
            error = "Lista de modos ausente ou grande demais.";
            return null;
        }

        var map = new Dictionary<OperatingMode, GpuClockOffset>();
        foreach (var m in Modes)
        {
            if (m is null || !map.TryAdd(m.Mode, new GpuClockOffset(m.CoreMhz, m.MemoryMhz)))
            {
                error = "Modo repetido ou vazio.";
                return null;
            }
        }

        var settings = new GpuOffsetSettings(Enabled, map);
        error = settings.Validate();
        return error is null ? settings : null;
    }
}
