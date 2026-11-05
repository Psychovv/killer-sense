using Avalonia;
using Avalonia.Media;

namespace KillerSense.App.Settings;

/// <summary>
/// Cor de destaque. Todas as telas usam o mesmo pincel <c>KsAccentBrush</c> (StaticResource devolve a
/// mesma instância), então trocar a cor dele atualiza tudo na hora.
/// </summary>
public static class AccentColor
{
    private static readonly SolidColorBrush Fallback = new(Color.Parse(AppSettings.DefaultAccent));

    public static SolidColorBrush Brush =>
        Application.Current?.Resources.TryGetResource("KsAccentBrush", null, out var value) == true && value is SolidColorBrush brush
            ? brush
            : Fallback;

    public static void Apply(string hex)
    {
        if (Color.TryParse(hex, out var color))
        {
            Brush.Color = color;
        }
    }
}
