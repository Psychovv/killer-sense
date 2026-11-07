using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KillerSense.App.Platform;
using KillerSense.App.Settings;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Configurações: iniciar com o Windows (atalho na pasta Inicializar), unidade de temperatura, cor de
/// destaque, pasta de logs e sobre. Tudo só da interface: nada passa pelo serviço nem pelo firmware.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public static readonly string LogsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KillerSense", "logs");

    private bool _loading;

    public SettingsViewModel()
    {
        Accents =
        [
            new(this, "Violeta", AppSettings.DefaultAccent),
            new(this, "Aço", AppTheme.DefaultAccentFor(AppTheme.Metal)),
            new(this, "Vermelho", "#E5262D"),
            new(this, "Ciano", "#5EC8FF"),
            new(this, "Verde", "#4DD68B"),
            new(this, "Laranja", "#FF9F4D"),
            new(this, "Rosa", "#FF6BB5"),
        ];
        var version = typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "—";
        Version = version.Split('+')[0]; // sem o hash do commit
        SelectedAccent = Accents[0];
        Reload();
    }

    public string Version { get; }

    public string LogsFolder { get; } = LogsPath;

    public IReadOnlyList<AccentViewModel> Accents { get; }

    // Geral
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool StartInTray { get; set; }

    // Unidades
    [ObservableProperty] public partial bool UseCelsius { get; set; }
    [ObservableProperty] public partial bool UseFahrenheit { get; set; }

    // Aparência
    [ObservableProperty] public partial bool UseGrafite { get; set; }
    [ObservableProperty] public partial bool UseMetal { get; set; }
    [ObservableProperty] public partial AccentViewModel SelectedAccent { get; set; }

    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool StatusIsError { get; set; }

    /// <summary>Mostra o que está valendo: o arquivo de preferências e o atalho que existe de fato.</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            var s = AppSettings.Current;
            StartWithWindows = StartupShortcut.Exists;
            StartInTray = s.StartInTray;
            UseFahrenheit = s.Fahrenheit;
            UseCelsius = !s.Fahrenheit;
            UseMetal = s.Theme == AppTheme.Metal;
            UseGrafite = !UseMetal;
            SelectedAccent = Accents.FirstOrDefault(a => string.Equals(a.Hex, s.Accent, StringComparison.OrdinalIgnoreCase)) ?? Accents[0];
        }
        finally
        {
            _loading = false;
        }

        IsDirty = false;
    }

    partial void OnSelectedAccentChanged(AccentViewModel oldValue, AccentViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        newValue.IsSelected = true;
        Changed();
    }

    partial void OnStartWithWindowsChanged(bool value) => Changed();

    partial void OnStartInTrayChanged(bool value) => Changed();

    partial void OnUseCelsiusChanged(bool value) => Changed();

    partial void OnUseFahrenheitChanged(bool value) => Changed();

    partial void OnUseGrafiteChanged(bool value) => ThemePicked(value, AppTheme.Grafite, AppTheme.Metal);

    partial void OnUseMetalChanged(bool value) => ThemePicked(value, AppTheme.Metal, AppTheme.Grafite);

    /// <summary>
    /// Ao trocar de tema, se o destaque ainda era o padrão do outro tema, troca pelo padrão deste
    /// (Violeta no Grafite, Aço no Metal). Um destaque escolhido à mão fica.
    /// </summary>
    private void ThemePicked(bool picked, string theme, string other)
    {
        if (!picked || _loading)
        {
            return;
        }

        if (string.Equals(SelectedAccent.Hex, AppTheme.DefaultAccentFor(other), StringComparison.OrdinalIgnoreCase)
            && Accents.FirstOrDefault(a => string.Equals(a.Hex, AppTheme.DefaultAccentFor(theme), StringComparison.OrdinalIgnoreCase)) is { } match)
        {
            SelectedAccent = match;
        }

        Changed();
    }

    private void Changed()
    {
        if (_loading)
        {
            return;
        }

        IsDirty = true;
        SetStatus(null, false);
    }

    [RelayCommand]
    private void Save()
    {
        var s = AppSettings.Current;
        s.Fahrenheit = UseFahrenheit;
        s.Accent = SelectedAccent.Hex;
        s.StartInTray = StartInTray;
        s.Theme = UseMetal ? AppTheme.Metal : AppTheme.Grafite;
        AppTheme.Apply(s.Theme);
        AccentColor.Apply(s.Accent);

        var problems = new List<string>();
        try
        {
            s.Save(AppSettings.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"não foi possível gravar as preferências ({ex.Message})");
        }

        try
        {
            if (StartWithWindows)
            {
                StartupShortcut.Create(StartInTray);
            }
            else
            {
                StartupShortcut.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            problems.Add($"não foi possível {(StartWithWindows ? "criar" : "apagar")} o atalho de inicialização ({ex.Message})");
        }

        Reload();
        if (problems.Count == 0)
        {
            SetStatus("Configurações salvas.", false);
        }
        else
        {
            IsDirty = true;
            SetStatus("Erro: " + string.Join("; ", problems) + ".", true);
        }
    }

    [RelayCommand]
    private void OpenLogs()
    {
        if (!Directory.Exists(LogsPath))
        {
            SetStatus($"A pasta {LogsPath} ainda não existe (o serviço cria ao iniciar).", true);
            return;
        }

        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { LogsPath }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus($"Não foi possível abrir a pasta: {ex.Message}", true);
        }
    }

    private void SetStatus(string? text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }
}

/// <summary>Uma cor de destaque.</summary>
public sealed partial class AccentViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;

    public AccentViewModel(SettingsViewModel owner, string name, string hex)
    {
        _owner = owner;
        Name = name;
        Hex = hex;
        Color = Color.Parse(hex);
    }

    public string Name { get; }

    public string Hex { get; }

    public Color Color { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => _owner.SelectedAccent = this;
}
