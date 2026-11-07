using System.Globalization;

namespace KillerSense.Core;

/// <summary>
/// Efeito da luz do teclado de 4 zonas (valor de <c>EC.KBLE</c>). Os números 1–7 vêm da tabela de modos da
/// <c>AcerECKeyboardController.dll</c> (CONFIRMADO no código); 0 é o estático, com uma cor por zona.
/// Falta ver cada efeito no notebook (docs/testes-hardware.md, T16).
/// </summary>
public enum KeyboardEffect : byte
{
    Static = 0,
    Breathing = 1,
    Neon = 2,
    Wave = 3,
    Shifting = 4,
    Zoom = 5,
    Meteor = 6,
    Twinkling = 7,
}

/// <summary>Direção dos efeitos que andam (<c>EC.KBED</c>): enum <c>EFFECT_DIRECTION</c> do PredatorSense.</summary>
public enum LightDirection : byte
{
    Right = 1,
    Left = 2,
}

/// <summary>Cor RGB de 8 bits por canal.</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Black => default;

    public bool IsBlack => R == 0 && G == 0 && B == 0;

    /// <summary>Formato <c>#RRGGBB</c>.</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();

    /// <summary>Aceita <c>#RRGGBB</c> ou <c>RRGGBB</c>.</summary>
    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        var s = text?.Trim();
        if (s is null)
        {
            return false;
        }

        if (s.StartsWith('#'))
        {
            s = s[1..];
        }

        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        color = new RgbColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }
}

/// <summary>Cor de cada uma das 4 zonas (da esquerda para a direita).</summary>
public readonly record struct ZoneColors(RgbColor Zone1, RgbColor Zone2, RgbColor Zone3, RgbColor Zone4)
{
    public const int Count = 4;

    public static ZoneColors All(RgbColor color) => new(color, color, color, color);

    public RgbColor this[int zone] => zone switch
    {
        0 => Zone1,
        1 => Zone2,
        2 => Zone3,
        3 => Zone4,
        _ => throw new ArgumentOutOfRangeException(nameof(zone)),
    };

    public ZoneColors With(int zone, RgbColor color) => zone switch
    {
        0 => this with { Zone1 = color },
        1 => this with { Zone2 = color },
        2 => this with { Zone3 = color },
        3 => this with { Zone4 = color },
        _ => throw new ArgumentOutOfRangeException(nameof(zone)),
    };
}

/// <summary>
/// Luz do teclado: efeito, brilho, velocidade, direção e cores. No estático cada zona tem cor própria e pode
/// ficar apagada (<see cref="ZonesOn"/>); nos efeitos animados as 4 zonas participam.
/// </summary>
/// <param name="Brightness">0–100 (<c>EC.KBBP</c>); 0 apaga o teclado.</param>
/// <param name="Speed">1–9 (<c>EC.KBLS</c>), só nos efeitos animados.</param>
/// <param name="Color">Cor dos efeitos de uma cor (respiração, deslocamento, zoom, meteoro, cintilante).</param>
/// <param name="ZonesOn">Zonas acesas no estático: bit 0 = zona 1 … bit 3 = zona 4 (<c>EC.KBST</c>).</param>
public sealed record KeyboardLighting(
    KeyboardEffect Effect,
    int Brightness,
    int Speed,
    LightDirection Direction,
    RgbColor Color,
    ZoneColors Zones,
    byte ZonesOn = KeyboardLighting.AllZones)
{
    public const int MinSpeed = 1;
    public const int MaxSpeed = 9;
    public const int DefaultSpeed = 5;
    public const int MaxBrightness = 100;
    public const byte AllZones = 0x0F;

    /// <summary>Ciano da Acer (cor de fábrica lida no snapshot 01).</summary>
    public static RgbColor AcerCyan { get; } = new(0x00, 0xAE, 0xC7);

    /// <summary>Estático, 100%, as 4 zonas em ciano.</summary>
    public static KeyboardLighting Default { get; } =
        new(KeyboardEffect.Static, MaxBrightness, DefaultSpeed, LightDirection.Right, AcerCyan, ZoneColors.All(AcerCyan));

    public static bool UsesSpeed(KeyboardEffect effect) => effect != KeyboardEffect.Static;

    public static bool UsesDirection(KeyboardEffect effect) => effect is KeyboardEffect.Wave or KeyboardEffect.Shifting;

    /// <summary>Efeitos de uma cor só (os outros animados escolhem as cores sozinhos).</summary>
    public static bool UsesColor(KeyboardEffect effect) => effect is KeyboardEffect.Breathing or KeyboardEffect.Shifting
        or KeyboardEffect.Zoom or KeyboardEffect.Meteor or KeyboardEffect.Twinkling;

    public bool IsZoneOn(int zone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(zone, ZoneColors.Count);
        return Effect != KeyboardEffect.Static || (ZonesOn & (1 << zone)) != 0;
    }

    /// <summary>Máscara gravada em <c>KBST</c>: no estático, as zonas acesas; nos animados, as 4.</summary>
    public byte ZoneMask => Effect == KeyboardEffect.Static ? (byte)(ZonesOn & AllZones) : AllZones;

    /// <summary>Null se estiver tudo dentro das faixas; senão, o motivo.</summary>
    public string? Validate()
    {
        if (!Enum.IsDefined(Effect))
        {
            return $"Efeito desconhecido: {(int)Effect}.";
        }

        if (Brightness is < 0 or > MaxBrightness)
        {
            return $"Brilho fora da faixa (0 a {MaxBrightness}): {Brightness}.";
        }

        if (Speed is < MinSpeed or > MaxSpeed)
        {
            return $"Velocidade fora da faixa ({MinSpeed} a {MaxSpeed}): {Speed}.";
        }

        if (!Enum.IsDefined(Direction))
        {
            return $"Direção desconhecida: {(int)Direction}.";
        }

        if ((ZonesOn & ~AllZones) != 0)
        {
            return $"Máscara de zonas inválida: 0x{ZonesOn:X2}.";
        }

        return null;
    }

    /// <summary>
    /// Compara só o que o efeito usa (ex.: a velocidade não importa no estático; as cores das zonas não
    /// importam nos animados). Serve para conferir a leitura do teclado depois de gravar.
    /// </summary>
    public bool SameVisibleAs(KeyboardLighting other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Effect != other.Effect || Brightness != other.Brightness)
        {
            return false;
        }

        if (UsesSpeed(Effect) && Speed != other.Speed)
        {
            return false;
        }

        if (UsesDirection(Effect) && Direction != other.Direction)
        {
            return false;
        }

        if (UsesColor(Effect) && Color != other.Color)
        {
            return false;
        }

        if (Effect == KeyboardEffect.Static)
        {
            for (var z = 0; z < ZoneColors.Count; z++)
            {
                var on = IsZoneOn(z);
                if (on != other.IsZoneOn(z) || (on && Zones[z] != other.Zones[z]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public override string ToString() => Effect == KeyboardEffect.Static
        ? $"{Effect}, brilho {Brightness}, zonas {Zones.Zone1}/{Zones.Zone2}/{Zones.Zone3}/{Zones.Zone4}, acesas 0x{ZonesOn:X}"
        : $"{Effect}, brilho {Brightness}, velocidade {Speed}, {Direction}, cor {Color}";
}

/// <summary>
/// Configuração da luz e se o killer-sense deve controlá-la. Padrão: DESLIGADO — sem nenhuma chamada ao
/// firmware da luz até o teste no notebook (docs/testes-hardware.md, T16).
/// </summary>
public sealed record LightingSettings(bool Enabled, KeyboardLighting Lighting)
{
    public static LightingSettings Default { get; } = new(false, KeyboardLighting.Default);

    public string? Validate() => Lighting is null ? "Configuração da luz ausente." : Lighting.Validate();
}
