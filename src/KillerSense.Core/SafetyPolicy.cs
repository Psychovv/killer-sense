namespace KillerSense.Core;

/// <summary>
/// Regras de segurança aprovadas (docs/arquitetura.md §5.4 e §6): pisos por modo térmico e limites de emergência.
/// </summary>
public static class SafetyPolicy
{
    /// <summary>Emergência: CPU ≥ 95 °C.</summary>
    public const double CpuEmergencyTemperature = 95;

    /// <summary>Emergência: GPU ≥ 87 °C (limite térmico da GPU lido no HWiNFO).</summary>
    public const double GpuEmergencyTemperature = 87;

    /// <summary>Sai da emergência quando cair esta margem abaixo do limite.</summary>
    public const double EmergencyReleaseMargin = 10;

    /// <summary>Leituras inválidas seguidas que fazem a fan voltar ao Auto.</summary>
    public const int MaxConsecutiveInvalidReadings = 3;

    /// <summary>Piso mínimo de rotação (%) para fans em curva, por modo térmico.</summary>
    public static double FloorPercent(OperatingMode mode) => mode switch
    {
        OperatingMode.Quiet or OperatingMode.Eco => 20,
        OperatingMode.Balanced => 25,
        OperatingMode.Performance => 35,
        OperatingMode.Turbo => 45,
        _ => 45, // modo desconhecido: o piso mais alto
    };
}

/// <summary>Detecta e libera o estado de emergência térmica, com histerese.</summary>
public sealed class EmergencyMonitor
{
    public bool IsActive { get; private set; }

    /// <summary>
    /// Atualiza com as temperaturas válidas atuais (null = sem leitura válida).
    /// Sem leitura válida nunca libera uma emergência ativa.
    /// </summary>
    public bool Update(double? cpuTemperature, double? gpuTemperature)
    {
        var cpuHot = cpuTemperature >= SafetyPolicy.CpuEmergencyTemperature;
        var gpuHot = gpuTemperature >= SafetyPolicy.GpuEmergencyTemperature;
        if (cpuHot || gpuHot)
        {
            IsActive = true;
            return IsActive;
        }

        if (IsActive)
        {
            var cpuCool = cpuTemperature is { } c && c <= SafetyPolicy.CpuEmergencyTemperature - SafetyPolicy.EmergencyReleaseMargin;
            var gpuCool = gpuTemperature is null || gpuTemperature <= SafetyPolicy.GpuEmergencyTemperature - SafetyPolicy.EmergencyReleaseMargin;
            if (cpuCool && gpuCool)
            {
                IsActive = false;
            }
        }

        return IsActive;
    }
}
