using Avalonia;

namespace KillerSense.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Usado também pelo designer do Avalonia.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Desenho pela CPU (Skia): não carrega o driver de vídeo (~100 MB a menos); a tela é simples
            // e atualiza 1 vez por segundo.
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
            .LogToTrace();
}
