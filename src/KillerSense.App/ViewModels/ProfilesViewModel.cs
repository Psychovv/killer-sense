using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Perfis (PRÉVIA, sem integração): um perfil junta modo, fans, iluminação, GPU e bateria. "Padrão
/// Acer" é somente leitura (fans no Auto do EC, sem offset além do Turbo). Lista só em memória; nada é
/// salvo nem aplicado.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    public ProfilesViewModel()
    {
        Profiles =
        [
            new(this, "Padrão Acer", modeIndex: 1, fanIndex: 0, lightingIndex: 0, gpuIndex: 0, batteryLimit: true, isReadOnly: true),
            new(this, "Silencioso para estudar", modeIndex: 0, fanIndex: 1, lightingIndex: 0, gpuIndex: 0, batteryLimit: true),
            new(this, "Jogo", modeIndex: 3, fanIndex: 1, lightingIndex: 2, gpuIndex: 1, batteryLimit: false),
        ];
        Selected = Profiles[1];
        Active = Profiles[1];
    }

    public ObservableCollection<ProfileViewModel> Profiles { get; }

    public IReadOnlyList<string> ModeOptions { get; } = ["Silencioso", "Equilibrado", "Desempenho", "Turbo", "Eco"];

    public IReadOnlyList<string> FanOptions { get; } = ["Auto do EC", "Curvas da tela Fans"];

    public IReadOnlyList<string> LightingOptions { get; } = ["Como está agora", "Desligada", "Configuração da tela Iluminação"];

    public IReadOnlyList<string> GpuOptions { get; } = ["Padrão Acer", "Offsets da tela GPU"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(ApplyCommand))]
    public partial ProfileViewModel? Selected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial ProfileViewModel? Active { get; set; }

    [ObservableProperty] public partial string? Status { get; set; }

    /// <summary>O perfil escolhido pode ser editado (o "Padrão Acer" não).</summary>
    public bool CanEdit => Selected is { IsReadOnly: false };

    partial void OnActiveChanged(ProfileViewModel? oldValue, ProfileViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }
    }

    partial void OnSelectedChanged(ProfileViewModel? value) => Status = null;

    [RelayCommand]
    private void New()
    {
        var profile = new ProfileViewModel(this, NextName("Novo perfil"), modeIndex: 1, fanIndex: 1, lightingIndex: 0, gpuIndex: 0, batteryLimit: true);
        Profiles.Add(profile);
        Selected = profile;
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is not { } source)
        {
            return;
        }

        var copy = new ProfileViewModel(this, NextName(source.Name + " (cópia)"), source.ModeIndex, source.FanIndex, source.LightingIndex, source.GpuIndex, source.BatteryLimit);
        Profiles.Add(copy);
        Selected = copy;
    }

    private bool CanDelete() => Selected is { IsReadOnly: false } && Selected != Active;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (Selected is not { } profile)
        {
            return;
        }

        var index = Profiles.IndexOf(profile);
        Profiles.Remove(profile);
        Selected = Profiles[Math.Min(index, Profiles.Count - 1)];
    }

    private bool CanApply() => Selected is not null && Selected != Active;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        Active = Selected;
        DeleteCommand.NotifyCanExecuteChanged();
        Status = $"Prévia: “{Active?.Name}” marcado como ativo, mas nada foi aplicado ao notebook.";
    }

    [RelayCommand]
    private void Export() => Status = "Prévia: exportar perfis (arquivo .json) ainda não está ligado.";

    [RelayCommand]
    private void Import() => Status = "Prévia: importar perfis (arquivo .json) ainda não está ligado.";

    internal void Edited() => Status = null;

    private string NextName(string baseName)
    {
        var name = baseName;
        for (var n = 2; Profiles.Any(p => p.Name == name); n++)
        {
            name = baseName + " " + n.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }
}

/// <summary>Um perfil da lista.</summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly ProfilesViewModel _owner;

    public ProfileViewModel(ProfilesViewModel owner, string name, int modeIndex, int fanIndex, int lightingIndex, int gpuIndex, bool batteryLimit, bool isReadOnly = false)
    {
        _owner = owner;
        Name = name;
        ModeIndex = modeIndex;
        FanIndex = fanIndex;
        LightingIndex = lightingIndex;
        GpuIndex = gpuIndex;
        BatteryLimit = batteryLimit;
        IsReadOnly = isReadOnly;
    }

    public bool IsReadOnly { get; }

    [ObservableProperty] public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial int FanIndex { get; set; }

    [ObservableProperty] public partial int LightingIndex { get; set; }
    [ObservableProperty] public partial int GpuIndex { get; set; }
    [ObservableProperty] public partial bool BatteryLimit { get; set; }
    [ObservableProperty] public partial bool IsActive { get; set; }

    /// <summary>Resumo de uma linha mostrado na lista.</summary>
    public string Summary
    {
        get
        {
            var mode = ModeIndex >= 0 && ModeIndex < _owner.ModeOptions.Count ? _owner.ModeOptions[ModeIndex] : "—";
            var fans = FanIndex == 0 ? "fans no Auto" : "fans na curva";
            return mode + " · " + fans + (IsReadOnly ? " · somente leitura" : "");
        }
    }

    partial void OnNameChanged(string value) => _owner.Edited();

    partial void OnModeIndexChanged(int value) => _owner.Edited();

    partial void OnFanIndexChanged(int value) => _owner.Edited();

    partial void OnLightingIndexChanged(int value) => _owner.Edited();

    partial void OnGpuIndexChanged(int value) => _owner.Edited();

    partial void OnBatteryLimitChanged(bool value) => _owner.Edited();
}
