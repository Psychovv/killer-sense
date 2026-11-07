using KillerSense.Core;

namespace KillerSense.Firmware;

/// <summary>
/// Codificação e decodificação dos valores de <c>AcerGamingFunction</c>, sem acesso a hardware.
/// Fonte: docs/acer-interface.md (ACPI WMBH, testes A4/A6/A7/A10 e engenharia reversa).
/// </summary>
public static class AcerProtocol
{
    /// <summary>Bit da fan na máscara do FanBehavior: CPU = bit 0, GPU = bit 3.</summary>
    public static int MaskBit(Fan fan) => fan switch
    {
        Fan.Cpu => 0,
        Fan.Gpu => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(fan)),
    };

    /// <summary>Id da fan no FanSpeed: CPU = 1, GPU = 4 (diferente da máscara!).</summary>
    public static byte SpeedId(Fan fan) => fan switch
    {
        Fan.Cpu => 1,
        Fan.Gpu => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(fan)),
    };

    /// <summary>Máscara de leitura com todas as fans dadas (ex.: CPU+GPU = 0x09).</summary>
    public static uint BehaviorMask(IEnumerable<Fan> fans)
    {
        ArgumentNullException.ThrowIfNull(fans);
        uint mask = 0;
        foreach (var fan in fans)
        {
            mask |= 1u << MaskBit(fan);
        }

        return mask;
    }

    /// <summary>
    /// Entrada de <c>SetGamingFanBehavior</c>: <c>(modos &lt;&lt; 16) | máscara</c>, com 2 bits de modo
    /// por fan na posição 2×bit. CPU+GPU: Auto = 0x00410009, Max = 0x00820009, Custom = 0x00C30009.
    /// </summary>
    public static ulong EncodeSetFanBehavior(IReadOnlyDictionary<Fan, FanMode> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        if (modes.Count == 0)
        {
            throw new ArgumentException("Informe ao menos uma fan.", nameof(modes));
        }

        ulong mask = 0;
        ulong modeBits = 0;
        foreach (var (fan, mode) in modes)
        {
            if (!Enum.IsDefined(mode))
            {
                throw new ArgumentOutOfRangeException(nameof(modes), $"Modo de fan inválido: {(int)mode}.");
            }

            var bit = MaskBit(fan);
            mask |= 1ul << bit;
            modeBits |= (ulong)mode << (2 * bit);
        }

        return (modeBits << 16) | mask;
    }

    /// <summary>
    /// Saída de <c>GetGamingFanBehavior</c>: byte0 = status; modo da fan nos bits <c>8 + 2×bit</c>.
    /// </summary>
    public static FanMode DecodeFanMode(ulong output, Fan fan)
    {
        EnsureStatusOk(output, "GetGamingFanBehavior");
        var raw = (byte)((output >> (8 + (2 * MaskBit(fan)))) & 0b11);
        return raw switch
        {
            1 => FanMode.Auto,
            2 => FanMode.Max,
            3 => FanMode.Custom,
            _ => throw new FirmwareException($"GetGamingFanBehavior devolveu modo {raw} para {fan} (saída 0x{output:X})."),
        };
    }

    /// <summary>Entrada de <c>SetGamingFanSpeed</c>: <c>(% &lt;&lt; 8) | id</c>. O ACPI recusa % &gt; 100.</summary>
    public static ulong EncodeSetFanSpeed(Fan fan, int percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        return ((ulong)percent << 8) | SpeedId(fan);
    }

    /// <summary>Saída de <c>GetGamingFanSpeed</c>: byte0 = status, byte1 = %.</summary>
    public static int DecodeFanSpeed(ulong output)
    {
        EnsureStatusOk(output, "GetGamingFanSpeed");
        return (int)((output >> 8) & 0xFF);
    }

    /// <summary>Entrada de <c>GetGamingSysInfo</c> para sensores do EC: byte0 = 1, byte1 = índice.</summary>
    public static uint EncodeSysInfo(Sensor sensor) => 1u | ((uint)sensor << 8);

    /// <summary>Saída de <c>GetGamingSysInfo</c> (grupo 1): byte0 = status, bytes 1–2 = valor de 16 bits.</summary>
    public static int DecodeSysInfo(ulong output)
    {
        EnsureStatusOk(output, "GetGamingSysInfo");
        return (int)((output >> 8) & 0xFFFF);
    }

    /// <summary>Entrada de <c>GetGamingMiscSetting</c> para o modo de operação.</summary>
    public const uint MiscOperatingMode = 0x0B;

    /// <summary>Saída de <c>GetGamingMiscSetting(0x0B)</c>: byte0 = status, byte1 = OPMS.</summary>
    public static OperatingMode DecodeOperatingMode(ulong output)
    {
        EnsureStatusOk(output, "GetGamingMiscSetting");
        var raw = (byte)((output >> 8) & 0xFF);
        if (!Enum.IsDefined((OperatingMode)raw))
        {
            throw new FirmwareException($"Modo de operação desconhecido: {raw}.");
        }

        return (OperatingMode)raw;
    }

    /// <summary>
    /// Entrada de <c>SetGamingMiscSetting</c> para o modo: <c>(modo &lt;&lt; 8) | 0x0B</c>
    /// (Silencioso 0x00B, Equilibrado 0x10B, Desempenho 0x40B, Turbo 0x50B, Eco 0x60B).
    /// </summary>
    public static ulong EncodeSetOperatingMode(OperatingMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), $"Modo de operação inválido: {(int)mode}.");
        }

        return ((ulong)mode << 8) | MiscOperatingMode;
    }

    /// <summary>
    /// Só o subcomando 0x0B (modo) com um modo conhecido é aceito em <c>SetGamingMiscSetting</c>. Os outros
    /// (0x01 TKST, 0x0F FMKY, 2/6/8 via SMI) ficam bloqueados.
    /// </summary>
    public static bool IsAllowedMiscSetting(ulong input) =>
        (input & 0xFF) == MiscOperatingMode && input >> 16 == 0 && Enum.IsDefined((OperatingMode)(byte)(input >> 8));

    // ---- Luz do teclado (4 zonas) — docs/acer-interface.md, "Teclado" ----

    /// <summary>Tamanho do <c>gmInput</c> (UInt8[16]) de <c>SetGamingLED</c> e <c>SetGamingKBBacklight</c>.</summary>
    public const int LightInputLength = 16;

    /// <summary>Subcomando de <c>Set/GetGamingLED</c> das zonas do teclado (<c>EC.KBST</c>).</summary>
    public const byte LedKeyboardZones = 0x08;

    /// <summary>Alvo de <c>Set/GetGamingKBBacklight</c>: 1 = teclado (2 = light bar, que este modelo não tem).</summary>
    public const byte BacklightKeyboard = 0x01;

    /// <summary>Valor de <c>KLES</c> que a DLL da Acer sempre manda.</summary>
    public const byte BacklightKles = 0x03;

    /// <summary>Máscara da zona no <c>Set/GetGamingRgbKb</c>: zona 1 = 1, 2 = 2, 3 = 4, 4 = 8.</summary>
    public static byte ZoneBit(int zone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(zone, ZoneColors.Count);
        return (byte)(1 << zone);
    }

    /// <summary><c>SetGamingLED</c>: <c>[0] = 8</c>, <c>[5]</c> = zonas acesas (vai para <c>EC.KBST</c>).</summary>
    public static byte[] EncodeSetKeyboardZones(byte zoneMask)
    {
        if ((zoneMask & ~KeyboardLighting.AllZones) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoneMask), $"Máscara de zonas inválida: 0x{zoneMask:X2}.");
        }

        var input = new byte[LightInputLength];
        input[0] = LedKeyboardZones;
        input[5] = zoneMask;
        return input;
    }

    /// <summary>
    /// <c>SetGamingKBBacklight</c> do teclado: <c>[0]</c> efeito, <c>[1]</c> velocidade (0 no estático, como a
    /// Acer), <c>[2]</c> brilho, <c>[3] = 0</c>, <c>[4]</c> direção, <c>[5..7]</c> RGB, <c>[8]</c> KLES = 3,
    /// <c>[9]</c> alvo = 1. Nenhum byte vale 0xFF ("não mudar" no ACPI).
    /// </summary>
    public static byte[] EncodeSetKeyboardBacklight(KeyboardLighting lighting)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        if (lighting.Validate() is { } error)
        {
            throw new ArgumentException(error, nameof(lighting));
        }

        var effect = lighting.Effect;
        var color = KeyboardLighting.UsesColor(effect) ? lighting.Color : RgbColor.Black;
        var input = new byte[LightInputLength];
        input[0] = (byte)effect;
        input[1] = KeyboardLighting.UsesSpeed(effect) ? (byte)lighting.Speed : (byte)0;
        input[2] = (byte)lighting.Brightness;
        input[3] = 0;
        input[4] = KeyboardLighting.UsesDirection(effect) ? (byte)lighting.Direction : (byte)LightDirection.Right;
        input[5] = color.R;
        input[6] = color.G;
        input[7] = color.B;
        input[8] = BacklightKles;
        input[9] = BacklightKeyboard;
        return input;
    }

    /// <summary><c>SetGamingRgbKb</c>: <c>máscara_zona | R &lt;&lt; 8 | G &lt;&lt; 16 | B &lt;&lt; 24</c>.</summary>
    public static ulong EncodeSetZoneColor(int zone, RgbColor color) =>
        ZoneBit(zone) | ((ulong)color.R << 8) | ((ulong)color.G << 16) | ((ulong)color.B << 24);

    /// <summary>Saída de <c>GetGamingRgbKb(máscara)</c>: byte0 = status, bytes 1–3 = R, G, B.</summary>
    public static RgbColor DecodeZoneColor(ulong output)
    {
        EnsureStatusOk(output, "GetGamingRgbKb");
        return new RgbColor((byte)(output >> 8), (byte)(output >> 16), (byte)(output >> 24));
    }

    /// <summary>
    /// Saída de <c>GetGamingKBBacklight(1)</c> (<c>gmOutput</c> = bytes 1..15 do buffer ACPI): <c>[0]</c> KBLE,
    /// <c>[1]</c> KBLS, <c>[2]</c> KBBP, <c>[3] = 0xFF</c>, <c>[4]</c> KBED, <c>[5..7]</c> RGB, <c>[8]</c> KLES.
    /// Junta com as cores e a máscara das zonas. Velocidade e direção fora da faixa (ex.: 0 no estático)
    /// viram o padrão; efeito desconhecido vira <see cref="FirmwareException"/>.
    /// </summary>
    public static KeyboardLighting DecodeKeyboardLighting(byte status, byte[] backlight, ZoneColors zones, byte zoneMask)
    {
        ArgumentNullException.ThrowIfNull(backlight);
        if (status != 0)
        {
            throw new FirmwareException($"GetGamingKBBacklight devolveu status {status}.");
        }

        if (backlight.Length < 9)
        {
            throw new FirmwareException($"GetGamingKBBacklight devolveu {backlight.Length} bytes (esperado 15).");
        }

        var effect = (KeyboardEffect)backlight[0];
        if (!Enum.IsDefined(effect))
        {
            throw new FirmwareException($"Efeito da luz desconhecido no EC: {backlight[0]}.");
        }

        var speed = backlight[1] is >= KeyboardLighting.MinSpeed and <= KeyboardLighting.MaxSpeed ? backlight[1] : KeyboardLighting.DefaultSpeed;
        var direction = Enum.IsDefined((LightDirection)backlight[4]) ? (LightDirection)backlight[4] : LightDirection.Right;
        var brightness = Math.Min((int)backlight[2], KeyboardLighting.MaxBrightness);
        return new KeyboardLighting(
            effect,
            brightness,
            speed,
            direction,
            new RgbColor(backlight[5], backlight[6], backlight[7]),
            zones,
            (byte)(zoneMask & KeyboardLighting.AllZones));
    }

    /// <summary>Saída de <c>GetGamingLED(8)</c>: <c>gmOutput[4]</c> = <c>EC.KBST</c> (zonas acesas).</summary>
    public static byte DecodeKeyboardZones(byte status, byte[] output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (status != 0)
        {
            throw new FirmwareException($"GetGamingLED devolveu status {status}.");
        }

        return output.Length > 4
            ? (byte)(output[4] & KeyboardLighting.AllZones)
            : throw new FirmwareException($"GetGamingLED devolveu {output.Length} bytes (esperado 15).");
    }

    /// <summary>
    /// Só as zonas do teclado em <c>SetGamingLED</c>: <c>[0] = 8</c>, máscara de 4 bits e o resto zerado.
    /// <c>[0] = 1</c> (TKST, modo Turbo) e <c>0x10</c> (light bar) ficam bloqueados.
    /// </summary>
    public static bool IsAllowedSetLed(byte[] input) =>
        input is { Length: LightInputLength }
        && input[0] == LedKeyboardZones
        && (input[5] & ~KeyboardLighting.AllZones) == 0
        && input.Select((b, i) => i is 0 or 5 || b == 0).All(ok => ok);

    /// <summary>Só o teclado (<c>[9] = 1</c>) em <c>SetGamingKBBacklight</c>, com efeito, velocidade, brilho e direção válidos.</summary>
    public static bool IsAllowedSetBacklight(byte[] input) =>
        input is { Length: LightInputLength }
        && input[9] == BacklightKeyboard
        && Enum.IsDefined((KeyboardEffect)input[0])
        && input[1] <= KeyboardLighting.MaxSpeed
        && input[2] <= KeyboardLighting.MaxBrightness
        && input[3] == 0
        && Enum.IsDefined((LightDirection)input[4])
        && input[8] == BacklightKles
        && input.Skip(10).All(b => b == 0);

    /// <summary>Só uma zona do teclado (máscara 1, 2, 4 ou 8) em <c>SetGamingRgbKb</c>; light bar bloqueada.</summary>
    public static bool IsAllowedSetZoneColor(ulong input) =>
        (input & 0xFF) is 1 or 2 or 4 or 8 && input >> 32 == 0;

    /// <summary>Status no byte 0 (Get e Set): 0 = OK; qualquer outro valor vira <see cref="FirmwareException"/>.</summary>
    public static void EnsureStatusOk(ulong output, string method)
    {
        if ((output & 0xFF) != 0)
        {
            throw new FirmwareException($"{method} devolveu status {output & 0xFF} (saída 0x{output:X}).");
        }
    }
}
