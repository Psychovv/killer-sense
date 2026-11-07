using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Perfis: um perfil junta modo, fans, offset da GPU, limite da bateria e luz do teclado, cada parte
/// podendo ficar "como está". Os perfis ficam na interface (<c>%APPDATA%\KillerSense\profiles.json</c>) e são
/// aplicados pelos comandos que o serviço já valida. "Padrão Acer" é somente leitura.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private readonly ServiceClient? _client;
    private readonly DashboardViewModel? _dashboard;
    private readonly string? _path;

    /// <summary>Só para o designer/prévia.</summary>
    public ProfilesViewModel()
        : this(null, null, null)
    {
    }

    /// <param name="path">Arquivo dos perfis; null = só em memória.</param>
    public ProfilesViewModel(ServiceClient? client, DashboardViewModel? dashboard, string? path)
    {
        _client = client;
        _dashboard = dashboard;
        _path = path;
        string? problem = null;
        var set = path is null ? UserProfileSet.Empty : UserProfileFile.Load(path, out problem);
        Profiles = [new ProfileViewModel(this, UserProfile.AcerDefault, isReadOnly: true), .. set.Profiles.Select(p => new ProfileViewModel(this, p))];
        Active = Profiles.FirstOrDefault(p => p.Name == set.LastApplied);
        Selected = Active ?? Profiles[0];
        IsDirty = false;
        SetStatus(problem, problem is not null);
    }

    /// <summary>Depois de aplicar um perfil (as outras telas recarregam o que mudou).</summary>
    public event Action? Applied;

    public ObservableCollection<ProfileViewModel> Profiles { get; }

    public static IReadOnlyList<string> ModeOptions { get; } = ["Não mudar", "Silencioso", "Equilibrado", "Desempenho", "Turbo", "Eco"];

    internal static readonly OperatingMode?[] ModeValues = [null, OperatingMode.Quiet, OperatingMode.Balanced, OperatingMode.Performance, OperatingMode.Turbo, OperatingMode.Eco];

    public static IReadOnlyList<string> FanOptions { get; } = ["Não mudar", "Auto do EC", "Curva guardada no perfil"];

    public static IReadOnlyList<string> GpuOptions { get; } = ["Não mudar", "Guardado no perfil"];

    public static IReadOnlyList<string> BatteryOptions { get; } = ["Não mudar", "Limitar a carga em 80%", "Carga completa"];

    public static IReadOnlyList<string> LightingOptions { get; } = ["Não mudar", "Guardada no perfil"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(ApplyCommand), nameof(CaptureCommand), nameof(DuplicateCommand))]
    public partial ProfileViewModel? Selected { get; set; }

    [ObservableProperty] public partial ProfileViewModel? Active { get; set; }

    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool StatusIsError { get; set; }

    /// <summary>Há mudanças na lista ainda não gravadas no arquivo.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(CaptureCommand), nameof(NewCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>O perfil escolhido pode ser editado (o "Padrão Acer" não).</summary>
    public bool CanEdit => Selected is { IsReadOnly: false };

    /// <summary>Perfis do usuário (sem o "Padrão Acer"), no formato do arquivo.</summary>
    public UserProfileSet ToSet() => new([.. Profiles.Where(p => !p.IsReadOnly).Select(p => p.ToModel())], Active?.Name);

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

    internal void Edited()
    {
        IsDirty = true;
        SetStatus(null, false);
    }

    /// <summary>Novo perfil com a configuração atual do killer-sense (o que der para ler).</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task NewAsync()
    {
        var profile = new ProfileViewModel(this, new UserProfile(NextName("Novo perfil")));
        Profiles.Add(profile);
        Selected = profile;
        Edited();
        await CaptureIntoAsync(profile).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (Selected is not { } source)
        {
            return;
        }

        var copy = new ProfileViewModel(this, source.ToModel() with { Name = NextName(source.Name + " (cópia)") });
        Profiles.Add(copy);
        Selected = copy;
        Edited();
    }

    private bool CanDelete() => Selected is { IsReadOnly: false };

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (Selected is not { IsReadOnly: false } profile)
        {
            return;
        }

        var index = Profiles.IndexOf(profile);
        Profiles.Remove(profile);
        if (Active == profile)
        {
            Active = null;
        }

        Selected = Profiles[Math.Min(index, Profiles.Count - 1)];
        Edited();
    }

    /// <summary>Troca o perfil escolhido pela configuração atual (modo, curva, GPU, bateria e luz).</summary>
    [RelayCommand(CanExecute = nameof(CanCapture))]
    private Task CaptureAsync() => Selected is { IsReadOnly: false } p ? CaptureIntoAsync(p) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save() => TrySave();

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAsync() => Selected is { } p ? ApplyProfileAsync(p) : Task.CompletedTask;

    /// <summary>Aplica o perfil com esse nome (menu da bandeja).</summary>
    public Task ApplyByNameAsync(string name) =>
        Profiles.FirstOrDefault(p => p.Name == name) is { } p ? ApplyProfileAsync(p) : Task.CompletedTask;

    /// <summary>Conteúdo do arquivo de exportação (só os perfis do usuário).</summary>
    public string Export()
    {
        var set = ToSet() with { LastApplied = null };
        if (set.Validate() is { } error)
        {
            throw new InvalidOperationException(error);
        }

        return IpcProtocol.SerializeUserProfiles(set);
    }

    /// <summary>Junta os perfis de um arquivo exportado (nomes repetidos ganham um número).</summary>
    public void Import(string json)
    {
        if (UserProfileFile.Parse(json, out var error) is not { } set)
        {
            SetStatus($"Não foi possível importar: {error}", true);
            return;
        }

        var room = UserProfileSet.MaxProfiles - (Profiles.Count - 1);
        var added = 0;
        foreach (var p in set.Profiles.Take(room))
        {
            var name = NextName(p.Name);
            if (name.Length > UserProfile.MaxNameLength)
            {
                name = NextName(p.Name[..(UserProfile.MaxNameLength - 4)]);
            }

            Profiles.Add(new ProfileViewModel(this, p with { Name = name }));
            added++;
        }

        if (added > 0)
        {
            Selected = Profiles[^1];
        }

        IsDirty = added > 0;
        var skipped = set.Profiles.Count - added;
        SetStatus($"{added} perfil(is) importado(s){(skipped > 0 ? $"; {skipped} ficaram de fora (máximo de {UserProfileSet.MaxProfiles})" : "")}. Clique em Salvar para guardar.", skipped > 0);
    }

    private bool NotBusy() => !IsBusy;

    private bool HasSelection() => Selected is not null;

    private bool CanCapture() => Selected is { IsReadOnly: false } && !IsBusy;

    private bool CanApply() => Selected is not null && !IsBusy;

    private bool TrySave()
    {
        var set = ToSet();
        if (set.Validate() is { } error)
        {
            SetStatus(error, true);
            return false;
        }

        try
        {
            if (_path is not null)
            {
                UserProfileFile.Save(_path, set);
            }

            IsDirty = false;
            SetStatus("Perfis salvos.", false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Não foi possível salvar os perfis: {ex.Message}", true);
            return false;
        }
    }

    private async Task ApplyProfileAsync(ProfileViewModel profile)
    {
        if (_client is null || IsBusy)
        {
            return;
        }

        var model = profile.ToModel();
        if (model.Validate() is { } invalid)
        {
            SetStatus(invalid, true);
            return;
        }

        IsBusy = true;
        try
        {
            var results = await ProfileApplier.ApplyAsync(model, r => _client.RequestAsync(r, Timeout), _dashboard?.OnAc).ConfigureAwait(true);
            Active = profile;
            var unsaved = false;
            if (IsDirty)
            {
                unsaved = !TrySave(); // grava as edições junto com o "último aplicado"
            }
            else if (_path is not null)
            {
                TrySaveQuietly();
            }

            SetStatus(ProfileApplier.Summarize(profile.Name, results) + (unsaved ? " Os perfis não foram salvos." : ""), unsaved || results.Any(r => !r.Ok));
            Applied?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void TrySaveQuietly()
    {
        try
        {
            UserProfileFile.Save(_path!, ToSet());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // o perfil foi aplicado; só a marca de "último aplicado" não foi gravada
        }
    }

    private async Task CaptureIntoAsync(ProfileViewModel profile)
    {
        if (_client is null || _dashboard is not { IsConnected: true } dash)
        {
            SetStatus("Sem conexão com o serviço: o perfil começa com tudo em “Não mudar”.", true);
            return;
        }

        IsBusy = true;
        var missing = new List<string>();
        try
        {
            var mode = dash.CurrentMode;
            FanProfileDto? curve = null;
            GpuOffsetSettingsDto? gpu = null;
            KeyboardLightingDto? lighting = null;
            if (!dash.IsPaused)
            {
                curve = (await TryRequestAsync(IpcRequestType.GetProfile).ConfigureAwait(true))?.Profile;
                if (curve is null)
                {
                    missing.Add("curva das fans");
                }
            }

            gpu = (await TryRequestAsync(IpcRequestType.GetGpuOffsets).ConfigureAwait(true))?.GpuOffsets;
            if (gpu is null)
            {
                missing.Add("offset da GPU");
            }

            lighting = (await TryRequestAsync(IpcRequestType.GetLighting).ConfigureAwait(true))?.Lighting?.Lighting;
            if (lighting is null)
            {
                missing.Add("luz");
            }

            profile.Capture(mode, dash.IsPaused ? new ProfileFans(Auto: true) : curve is null ? null : new ProfileFans(false, curve), gpu, dash.ChargeLimitOn, lighting);
            Edited();
            SetStatus(missing.Count == 0
                ? "Configuração atual guardada no perfil (ainda não salva)."
                : $"Configuração atual guardada no perfil, menos: {string.Join(", ", missing)}.", missing.Count > 0);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<IpcMessage?> TryRequestAsync(IpcRequestType type)
    {
        try
        {
            return await _client!.RequestAsync(new IpcRequest(type), Timeout).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            return null;
        }
    }

    private void SetStatus(string? text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }

    private string NextName(string baseName)
    {
        var name = baseName.Trim();
        for (var n = 2; Profiles.Any(p => string.Equals(p.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase)); n++)
        {
            name = baseName.Trim() + " " + n.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }
}

/// <summary>Um perfil da lista (editável, menos o "Padrão Acer").</summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly ProfilesViewModel _owner;
    private bool _loading;

    public ProfileViewModel(ProfilesViewModel owner, UserProfile model, bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        _owner = owner;
        IsReadOnly = isReadOnly;
        _loading = true;
        Name = model.Name;
        Load(model);
        _loading = false;
    }

    public bool IsReadOnly { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(FansText), nameof(Problem))]
    public partial int FanIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(GpuText), nameof(Problem))]
    public partial int GpuIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial int BatteryIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(LightingText), nameof(Problem))]
    public partial int LightingIndex { get; set; }

    [ObservableProperty] public partial bool IsActive { get; set; }

    /// <summary>Curva guardada (vale com <see cref="FanIndex"/> = 2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FansText), nameof(Problem))]
    public partial FanProfileDto? Curve { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GpuText), nameof(Problem))]
    public partial GpuOffsetSettingsDto? Gpu { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LightingText), nameof(Problem))]
    public partial KeyboardLightingDto? Lighting { get; set; }

    /// <summary>Resumo de uma linha mostrado na lista.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (ProfilesViewModel.ModeValues[Math.Clamp(ModeIndex, 0, ProfilesViewModel.ModeValues.Length - 1)] is { } mode)
            {
                parts.Add(ProfileApplier.ModeName(mode));
            }

            parts.Add(FanIndex switch { 1 => "fans no Auto", 2 => "fans na curva", _ => "fans como estão" });
            if (GpuIndex == 1 && Gpu is not null)
            {
                parts.Add(Gpu.Enabled ? "GPU por modo" : "GPU sem offset");
            }

            if (BatteryIndex > 0)
            {
                parts.Add(BatteryIndex == 1 ? "80%" : "100%");
            }

            if (LightingIndex == 1 && Lighting is not null)
            {
                parts.Add("luz");
            }

            return string.Join(" · ", parts) + (IsReadOnly ? " · somente leitura" : "");
        }
    }

    public string FansText => FanIndex switch
    {
        1 => "Fans no Auto do EC (o killer-sense pausa a curva).",
        2 when Curve is { } c => $"Curva guardada: CPU entra a {c.CpuEntryTemperature:0} °C ({c.CpuCurve.Count} pontos), GPU a {c.GpuEntryTemperature:0} °C ({c.GpuCurve.Count} pontos).",
        2 => "Nenhuma curva guardada: use “Pegar a configuração atual”.",
        _ => "As fans ficam como estão.",
    };

    public string GpuText => GpuIndex switch
    {
        1 when Gpu is { } g => g.Enabled
            ? "Offset por modo ligado: " + string.Join(", ", g.Modes.Where(m => m.CoreMhz != 0 || m.MemoryMhz != 0).Select(m => $"{ProfileApplier.ModeName(m.Mode)} {m.CoreMhz:+0;-0;0}/{m.MemoryMhz:+0;-0;0} MHz").DefaultIfEmpty("todos em zero"))
            : "Offset da GPU desligado.",
        1 => "Nenhum offset guardado: use “Pegar a configuração atual”.",
        _ => "O offset da GPU fica como está.",
    };

    public string LightingText => LightingIndex switch
    {
        1 when Lighting is { } l => $"Luz guardada: {LightingViewModel.Effects.FirstOrDefault(e => e.Effect == l.Effect)?.Name ?? l.Effect.ToString()}, brilho {l.Brightness}%. Só vai ao teclado com “Controlar a luz” ligado na tela Iluminação.",
        1 => "Nenhuma luz guardada: use “Pegar a configuração atual”.",
        _ => "A luz fica como está.",
    };

    /// <summary>O que impede de salvar ou aplicar (null = nada).</summary>
    public string? Problem => ToModel().Validate()
        ?? (FanIndex == 2 && Curve is null ? "Escolha uma curva (Pegar a configuração atual) ou outra opção de fans."
            : GpuIndex == 1 && Gpu is null ? "Nenhum offset da GPU guardado."
            : LightingIndex == 1 && Lighting is null ? "Nenhuma luz guardada."
            : null);

    public UserProfile ToModel() => new(
        Name.Trim(),
        ProfilesViewModel.ModeValues[Math.Clamp(ModeIndex, 0, ProfilesViewModel.ModeValues.Length - 1)],
        FanIndex switch { 1 => new ProfileFans(Auto: true), 2 when Curve is not null => new ProfileFans(false, Curve), _ => null },
        GpuIndex == 1 ? Gpu : null,
        BatteryIndex switch { 1 => true, 2 => false, _ => null },
        LightingIndex == 1 ? Lighting : null);

    /// <summary>Guarda a configuração lida do serviço; parte não lida (null) fica "Não mudar".</summary>
    internal void Capture(OperatingMode? mode, ProfileFans? fans, GpuOffsetSettingsDto? gpu, bool? chargeLimit, KeyboardLightingDto? lighting) =>
        Load(new UserProfile(Name, mode, fans, gpu, chargeLimit, lighting));

    private void Load(UserProfile m)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            ModeIndex = Math.Max(0, Array.IndexOf(ProfilesViewModel.ModeValues, m.Mode));
            Curve = m.Fans?.Curve;
            FanIndex = m.Fans switch { null => 0, { Auto: true } => 1, _ => 2 };
            Gpu = m.GpuOffsets;
            GpuIndex = m.GpuOffsets is null ? 0 : 1;
            BatteryIndex = m.ChargeLimit switch { true => 1, false => 2, null => 0 };
            Lighting = m.Lighting;
            LightingIndex = m.Lighting is null ? 0 : 1;
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    partial void OnNameChanged(string value) => Changed();

    partial void OnModeIndexChanged(int value) => Changed();

    partial void OnFanIndexChanged(int value) => Changed();

    partial void OnGpuIndexChanged(int value) => Changed();

    partial void OnBatteryIndexChanged(int value) => Changed();

    partial void OnLightingIndexChanged(int value) => Changed();

    private void Changed()
    {
        if (!_loading && !IsReadOnly)
        {
            _owner.Edited();
        }
    }
}
