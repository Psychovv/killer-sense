using System.Reflection;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Configurações (PRÉVIA, sem integração): início com o Windows, unidades, aparência, serviço, logs e
/// sobre. As opções ficam só nesta janela; nada é gravado.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel()
    {
        Accents =
        [
            new(this, "Violeta", Color.Parse("#8B7BFF")),
            new(this, "Ciano", Color.Parse("#5EC8FF")),
            new(this, "Verde", Color.Parse("#4DD68B")),
            new(this, "Laranja", Color.Parse("#FF9F4D")),
            new(this, "Rosa", Color.Parse("#FF6BB5")),
        ];
        SelectedAccent = Accents[0];
        var version = typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "—";
        Version = version.Split('+')[0]; // sem o hash do commit
        IsDirty = false;
    }

    public string Version { get; }

    public IReadOnlyList<AccentViewModel> Accents { get; }

    public IReadOnlyList<string> LogLevels { get; } = ["Normal", "Detalhado (para diagnóstico)"];

    // Geral
    [ObservableProperty] public partial bool StartWithWindows { get; set; } = true;
    [ObservableProperty] public partial bool StartInTray { get; set; } = true;

    // Unidades
    [ObservableProperty] public partial bool UseCelsius { get; set; } = true;
    [ObservableProperty] public partial bool UseFahrenheit { get; set; }

    // Aparência
    [ObservableProperty] public partial AccentViewModel SelectedAccent { get; set; }

    // Serviço: intervalo do laço de controle (0,5 a 2 s; padrão 1 s, arquitetura §5.2)
    [ObservableProperty] public partial double LoopIntervalSeconds { get; set; } = 1;

    // Logs
    [ObservableProperty] public partial int LogLevelIndex { get; set; }

    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }

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

    partial void OnLoopIntervalSecondsChanged(double value) => Changed();

    partial void OnLogLevelIndexChanged(int value) => Changed();

    private void Changed()
    {
        IsDirty = true;
        Status = null;
    }

    [RelayCommand]
    private void Save()
    {
        IsDirty = false;
        Status = "Prévia: configurações guardadas só nesta janela. Nada foi gravado.";
    }

    [RelayCommand]
    private void OpenLogs() => Status = "Prévia: abrir a pasta de logs (C:\\ProgramData\\KillerSense\\logs) ainda não está ligado.";
}

/// <summary>Uma cor de destaque.</summary>
public sealed partial class AccentViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;

    public AccentViewModel(SettingsViewModel owner, string name, Color color)
    {
        _owner = owner;
        Name = name;
        Color = color;
    }

    public string Name { get; }

    public Color Color { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => _owner.SelectedAccent = this;
}
