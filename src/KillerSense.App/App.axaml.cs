using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using KillerSense.App.Platform;
using KillerSense.App.Settings;
using KillerSense.App.ViewModels;
using KillerSense.App.Views;
using KillerSense.Ipc;

namespace KillerSense.App;

/// <summary>
/// Aplicativo com ícone na bandeja: fechar a janela só a esconde na bandeja (a janela é destruída para
/// liberar memória); "Encerrar killer-sense" desativa o controle no serviço e devolve as fans ao Auto do
/// EC (PredatorSense no controle). Abrir o programa reativa o controle.
/// </summary>
public sealed class App : Application, IDisposable
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainViewModel? _main;
    private TrayIcon? _tray;
    private NativeMenuItem? _profilesMenu;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // --simulated: conecta no serviço rodando com firmware simulado (desenvolvimento).
            var simulated = desktop.Args?.Contains("--simulated") == true;
            var pipe = simulated ? IpcProtocol.SimulatedPipeName : IpcProtocol.PipeName;
            AppTheme.Apply(AppSettings.Current.Theme);
            AccentColor.Apply(AppSettings.Current.Accent);
            _main = new MainViewModel(new ServiceClient(pipe), UserProfileFile.DefaultPath(simulated));
            _main.Dashboard.PropertyChanged += UpdateTooltip;
            _main.Start();
            // Abrir o killer-sense = controle ativo. Aberto pelo Windows (--autostart) respeita o último
            // "Encerrar killer-sense": não retoma sozinho.
            if (desktop.Args?.Contains(StartupShortcut.AutostartArg) != true)
            {
                _main.EnableOnFirstConnection();
            }

            CreateTray();

            // --tray: inicia só na bandeja (ex.: com o Windows).
            if (desktop.Args?.Contains("--tray") != true)
            {
                ShowWindow();
            }

            desktop.Exit += (_, _) => Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTray()
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://KillerSense/Assets/killer-sense.ico"));
        var open = new NativeMenuItem("Abrir killer-sense");
        open.Click += (_, _) => ShowWindow();
        var pause = new NativeMenuItem("Voltar ao Auto (pausar)");
        pause.Click += (_, _) => _main?.Dashboard.RestoreAutoCommand.Execute(null);
        var quit = new NativeMenuItem("Encerrar killer-sense (devolver ao PredatorSense)");
        quit.Click += async (_, _) => await QuitAsync().ConfigureAwait(true);
        _profilesMenu = new NativeMenuItem("Perfis");
        RebuildProfilesMenu();
        if (_main is not null)
        {
            _main.Profiles.Profiles.CollectionChanged += (_, _) => RebuildProfilesMenu();
            _main.Profiles.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ProfilesViewModel.Active) or nameof(ProfilesViewModel.IsDirty))
                {
                    RebuildProfilesMenu();
                }
            };
        }

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "killer-sense",
            Menu = [open, _profilesMenu, pause, new NativeMenuItemSeparator(), quit],
        };
        _tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [_tray]);
    }

    /// <summary>Submenu da bandeja com os perfis (o último aplicado marcado); um clique aplica.</summary>
    private void RebuildProfilesMenu()
    {
        if (_profilesMenu is null || _main is null)
        {
            return;
        }

        var menu = new NativeMenu();
        foreach (var profile in _main.Profiles.Profiles)
        {
            var name = profile.Name;
            var item = new NativeMenuItem(name) { ToggleType = MenuItemToggleType.Radio, IsChecked = profile.IsActive };
            item.Click += async (_, _) => await _main.Profiles.ApplyByNameAsync(name).ConfigureAwait(true);
            menu.Add(item);
        }

        _profilesMenu.Menu = menu;
    }

    private void ShowWindow()
    {
        if (_desktop is null || _main is null)
        {
            return;
        }

        if (_desktop.MainWindow is not { } window)
        {
            window = new MainWindow { DataContext = _main };
            window.Closed += (_, _) =>
            {
                _desktop.MainWindow = null; // janela destruída; app segue na bandeja
                Dispatcher.UIThread.Post(MemoryTrim.AfterWindowClosed, DispatcherPriority.Background);
            };
            _desktop.MainWindow = window;
        }

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private async Task QuitAsync()
    {
        if (_main is not null)
        {
            await _main.DisableAsync().ConfigureAwait(true); // fans no Auto do EC, controle desativado
        }

        if (_tray is not null)
        {
            _tray.IsVisible = false;
        }

        _desktop?.Shutdown();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _tray = null;
        _main?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _main = null;
    }

    private void UpdateTooltip(object? sender, PropertyChangedEventArgs e)
    {
        if (_tray is null || _main is null || e.PropertyName is not (nameof(DashboardViewModel.StateText) or nameof(DashboardViewModel.CpuTemperatureText)))
        {
            return;
        }

        var d = _main.Dashboard;
        _tray.ToolTipText = d.IsConnected
            ? $"killer-sense — {d.StateText} · CPU {d.CpuTemperatureText} {Temperatures.Unit}"
            : "killer-sense — serviço indisponível";
    }
}
