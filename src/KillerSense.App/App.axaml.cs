using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KillerSense.App.ViewModels;
using KillerSense.App.Views;
using KillerSense.Ipc;

namespace KillerSense.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // --simulated: conecta no serviço rodando com firmware simulado (desenvolvimento).
            var pipe = desktop.Args?.Contains("--simulated") == true ? IpcProtocol.SimulatedPipeName : IpcProtocol.PipeName;
            var main = new MainViewModel(new ServiceClient(pipe));
            desktop.MainWindow = new MainWindow { DataContext = main };
            main.Start();
            desktop.Exit += (_, _) => main.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
