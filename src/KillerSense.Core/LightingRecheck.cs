namespace KillerSense.Core;

/// <summary>
/// Quando conferir a luz do teclado de novo. O serviço de luz da Acer (AcerLightingService) regrava o perfil
/// dele uns 5 s depois de o notebook acordar do standby (visto em 2026-11-07, docs/testes-hardware.md T16).
/// Depois de acordar, de iniciar o serviço ou de entrar na sessão, o killer-sense confere duas vezes e só
/// regrava se a luz do teclado não for a dele.
/// </summary>
public sealed class LightingRecheck
{
    /// <summary>
    /// Intervalo entre dois ciclos do serviço acima do qual o sistema esteve parado. O standby moderno não
    /// manda o aviso de suspensão ao serviço; o laço de 1 s simplesmente para e volta.
    /// </summary>
    public const double WakeGapSeconds = 5;

    /// <summary>Segundos depois do evento: a primeira depois da regravação da Acer, a segunda se ela demorar.</summary>
    public static IReadOnlyList<double> Delays { get; } = [10, 40];

    private int _next = -1;
    private double _elapsed;

    /// <summary>Ainda falta alguma conferência.</summary>
    public bool IsPending => _next >= 0;

    /// <summary>O que disparou as conferências (para o log).</summary>
    public string? Reason { get; private set; }

    /// <summary>Recomeça a contagem (um evento novo no meio da contagem adia as conferências).</summary>
    public void Start(string reason)
    {
        Reason = reason;
        _next = 0;
        _elapsed = 0;
    }

    /// <summary>Avança o tempo; true quando é hora de conferir.</summary>
    public bool Tick(double elapsedSeconds)
    {
        if (_next < 0)
        {
            return false;
        }

        _elapsed += elapsedSeconds;
        if (_elapsed < Delays[_next])
        {
            return false;
        }

        _next = _next + 1 < Delays.Count ? _next + 1 : -1;
        return true;
    }

    /// <summary>O sistema esteve parado entre dois ciclos (standby, hibernação).</summary>
    public static bool IsWakeGap(double secondsBetweenTicks) => secondsBetweenTicks > WakeGapSeconds;
}
