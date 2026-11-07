using KillerSense.Core;

namespace KillerSense.Ipc;

/// <summary>Luz do teclado serializável. Cores em <c>#RRGGBB</c>; zonas da esquerda para a direita.</summary>
public sealed record KeyboardLightingDto(
    KeyboardEffect Effect,
    int Brightness,
    int Speed,
    LightDirection Direction,
    string Color,
    IReadOnlyList<string> Zones,
    IReadOnlyList<bool>? ZonesOn = null)
{
    public static KeyboardLightingDto From(KeyboardLighting l)
    {
        ArgumentNullException.ThrowIfNull(l);
        var zones = new string[ZoneColors.Count];
        var on = new bool[ZoneColors.Count];
        for (var z = 0; z < ZoneColors.Count; z++)
        {
            zones[z] = l.Zones[z].ToHex();
            on[z] = (l.ZonesOn & (1 << z)) != 0;
        }

        return new(l.Effect, l.Brightness, l.Speed, l.Direction, l.Color.ToHex(), zones, on);
    }

    public KeyboardLighting? TryToLighting(out string? error)
    {
        if (!RgbColor.TryParse(Color, out var color))
        {
            error = $"Cor inválida: {Color}.";
            return null;
        }

        if (Zones is null || Zones.Count != ZoneColors.Count)
        {
            error = $"Informe a cor das {ZoneColors.Count} zonas.";
            return null;
        }

        if (ZonesOn is not null && ZonesOn.Count != ZoneColors.Count)
        {
            error = $"Informe se cada uma das {ZoneColors.Count} zonas fica acesa.";
            return null;
        }

        var zones = default(ZoneColors);
        byte mask = 0;
        for (var z = 0; z < ZoneColors.Count; z++)
        {
            if (!RgbColor.TryParse(Zones[z], out var c))
            {
                error = $"Cor inválida na zona {z + 1}: {Zones[z]}.";
                return null;
            }

            zones = zones.With(z, c);
            if (ZonesOn?[z] ?? true)
            {
                mask |= (byte)(1 << z);
            }
        }

        var lighting = new KeyboardLighting(Effect, Brightness, Speed, Direction, color, zones, mask);
        error = lighting.Validate();
        return error is null ? lighting : null;
    }
}

/// <summary>
/// Configuração da luz (pipe e <c>lighting.json</c>). Sempre validada por <see cref="TryToSettings"/> no serviço.
/// </summary>
public sealed record LightingSettingsDto(bool Enabled, KeyboardLightingDto Lighting)
{
    public static LightingSettingsDto From(LightingSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.Enabled, KeyboardLightingDto.From(s.Lighting));
    }

    public LightingSettings? TryToSettings(out string? error)
    {
        if (Lighting is null)
        {
            error = "Configuração da luz ausente.";
            return null;
        }

        return Lighting.TryToLighting(out error) is { } lighting ? new LightingSettings(Enabled, lighting) : null;
    }
}
