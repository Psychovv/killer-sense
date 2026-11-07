using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

namespace KillerSense.App.Settings;

/// <summary>
/// Tema da interface. As telas usam os pincéis <c>Ks*Brush</c> por StaticResource (sempre a mesma instância),
/// então trocar o tema muda a cor desses pincéis no lugar, como a <see cref="AccentColor"/>. Os raios dos cantos
/// são DynamicResource. O que só o Metal tem (títulos em itálico, faixa prateada no menu) vem da classe
/// <c>metal</c> na janela. A cor de destaque continua separada: é a que a pessoa escolheu.
/// </summary>
public static class AppTheme
{
    public const string Grafite = "grafite";
    public const string Metal = "metal";

    private static readonly Dictionary<string, string> GrafiteColors = new()
    {
        ["KsBg"] = "#1C1C20", ["KsSide"] = "#202026", ["KsCard"] = "#26262D", ["KsCard2"] = "#2C2C34",
        ["KsLine"] = "#34343D", ["KsText"] = "#F2F2F5", ["KsMuted"] = "#9A9AA6", ["KsAccent2"] = "#5EC8FF",
        ["KsWarn"] = "#F0B450", ["KsHot"] = "#FF6B7A",
    };

    // Preto grafite e prata da marca (docs/marca/icone-original.webp).
    private static readonly Dictionary<string, string> MetalColors = new()
    {
        ["KsBg"] = "#0B0B0C", ["KsSide"] = "#0F0F11", ["KsCard"] = "#151517", ["KsCard2"] = "#1D1D20",
        ["KsLine"] = "#2B2B30", ["KsText"] = "#ECECEF", ["KsMuted"] = "#8E8E97", ["KsAccent2"] = "#6F8FB8",
        ["KsWarn"] = "#F0B450", ["KsHot"] = "#FF4D4D",
    };

    // Prata escovada do logotipo (nome no menu lateral).
    private static readonly string[] MetalGradient = ["#F4F4F6", "#8D8F98", "#D9DADF", "#62646D"];

    public static string Current { get; private set; } = Grafite;

    public static bool IsMetal => Current == Metal;

    public static string Normalize(string? theme) => theme == Metal ? Metal : Grafite;

    /// <summary>Destaque que combina com o tema (usado ao trocar de tema na tela Configurações).</summary>
    public static string DefaultAccentFor(string theme) => Normalize(theme) == Metal ? "#D4D7DE" : AppSettings.DefaultAccent;

    public static void Apply(string? theme)
    {
        Current = Normalize(theme);
        if (Application.Current is not { } app)
        {
            return;
        }

        var colors = IsMetal ? MetalColors : GrafiteColors;
        foreach (var (key, hex) in colors)
        {
            if (app.Resources.TryGetResource(key + "Brush", null, out var value) && value is SolidColorBrush brush)
            {
                brush.Color = Color.Parse(hex);
            }
        }

        app.Resources["KsRadius"] = new CornerRadius(IsMetal ? 4 : 8);
        app.Resources["KsSmallRadius"] = new CornerRadius(IsMetal ? 4 : 6);
        app.Resources["KsChipRadius"] = new CornerRadius(IsMetal ? 4 : 999);

        if (app.Resources.TryGetResource("KsMetalBrush", null, out var metal) && metal is LinearGradientBrush gradient)
        {
            for (var i = 0; i < gradient.GradientStops.Count; i++)
            {
                // No Grafite o nome fica liso, na cor do texto.
                gradient.GradientStops[i].Color = Color.Parse(IsMetal ? MetalGradient[i % MetalGradient.Length] : colors["KsText"]);
            }
        }

        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                Mark(window);
            }
        }
    }

    /// <summary>Põe ou tira a classe <c>metal</c> da janela.</summary>
    public static void Mark(Window window) => window.Classes.Set(Metal, IsMetal);

    /// <summary>Pincel do tema pela chave (para controles desenhados no código).</summary>
    public static IBrush Brush(string key, IBrush fallback) =>
        Application.Current?.Resources.TryGetResource(key, null, out var value) == true && value is IBrush brush ? brush : fallback;
}
