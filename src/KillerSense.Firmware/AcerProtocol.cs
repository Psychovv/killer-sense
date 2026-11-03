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

    /// <summary>Status no byte 0 (Get e Set): 0 = OK; qualquer outro valor vira <see cref="FirmwareException"/>.</summary>
    public static void EnsureStatusOk(ulong output, string method)
    {
        if ((output & 0xFF) != 0)
        {
            throw new FirmwareException($"{method} devolveu status {output & 0xFF} (saída 0x{output:X}).");
        }
    }
}
