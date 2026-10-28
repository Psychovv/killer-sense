namespace KillerSense.Core;

/// <summary>Fans deste notebook. Ver docs/acer-interface.md (FanBehavior/FanSpeed).</summary>
public enum Fan
{
    Cpu,
    Gpu,
}

/// <summary>Modo de uma fan no firmware (2 bits por fan). CONFIRMADO: 1 = Auto, 2 = Max, 3 = Custom.</summary>
public enum FanMode : byte
{
    Auto = 1,
    Max = 2,
    Custom = 3,
}

/// <summary>Modo de operação (campo EC <c>OPMS</c>). CONFIRMADO pelos testes A1–A3 e pelo código do app.</summary>
public enum OperatingMode : byte
{
    Quiet = 0,
    Balanced = 1,
    Performance = 4,
    Turbo = 5,
    Eco = 6,
}

/// <summary>Sensores lidos via <c>GetGamingSysInfo</c> (índice no byte 1 da entrada).</summary>
public enum Sensor : byte
{
    CpuTemperature = 1,   // CTML — CONFIRMADO (A11)
    CpuFanRpm = 2,        // C1FR — CONFIRMADO (A4, A10)
    Ext1Temperature = 3,  // EST1
    GpuFanRpm = 6,        // G1FR — CONFIRMADO (A4, A10)
    Gpu2FanRpm = 9,       // G2FR — sempre 0 neste modelo
    SkinTemperature = 10, // SKTC — já devolveu 0 (inválido) esporadicamente
}

/// <summary>Limites físicos usados para validar leituras.</summary>
public static class PhysicalLimits
{
    public const double MinTemperature = 1;
    public const double MaxTemperature = 110;

    /// <summary>Teto observado no Max foi 6976 rpm (A4); acima de 8000 é leitura inválida.</summary>
    public const double MaxFanRpm = 8000;
}
