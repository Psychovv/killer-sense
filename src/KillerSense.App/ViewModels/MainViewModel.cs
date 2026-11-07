using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KillerSense.Ipc;

namespace KillerSense.App.ViewModels;

/// <summary>Janela principal: navegação entre as telas e a barra de estado comum.</summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    /// <param name="profilesPath">Arquivo dos perfis do usuário; null = só em memória.</param>
    public MainViewModel(ServiceClient client, string? profilesPath = null)
    {
        Dashboard = new DashboardViewModel(client);
        Fans = new FansViewModel(client, Dashboard);
        Modes = new ModesViewModel(client, Dashboard);
        Profiles = new ProfilesViewModel(client, Dashboard, profilesPath);
        Profiles.Applied += OnProfileApplied;
        Battery = new BatteryViewModel(client, Dashboard);
        Lighting = new LightingViewModel(client, Dashboard);
        Gpu = new GpuViewModel(client, Dashboard);
        Settings = new SettingsViewModel();

        Pages =
        [
            new(this, "Painel", Dashboard),
            new(this, "Fans", Fans),
            new(this, "Modos", Modes),
            new(this, "Perfis", Profiles),
            new(this, "Bateria", Battery),
            new(this, "Iluminação", Lighting),
            new(this, "GPU", Gpu),
            new(this, "Configurações", Settings),
        ];
        CurrentNav = Pages[0];
    }

    public DashboardViewModel Dashboard { get; }

    public FansViewModel Fans { get; }

    public ModesViewModel Modes { get; }

    public ProfilesViewModel Profiles { get; }

    public BatteryViewModel Battery { get; }

    public LightingViewModel Lighting { get; }


    public GpuViewModel Gpu { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Itens do menu lateral. Tecla de modos, USB desligado, overdrive e trava da tecla Windows ficam com o
    /// PredatorSense (decisão de 2026-11-07).
    /// </summary>
    public IReadOnlyList<NavItemViewModel> Pages { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage), nameof(Title))]
    public partial NavItemViewModel CurrentNav { get; set; }

    public ObservableObject CurrentPage => CurrentNav.Page;

    public string Title => CurrentNav.Title;

    partial void OnCurrentNavChanged(NavItemViewModel oldValue, NavItemViewModel newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsCurrent = false;
        }

        newValue.IsCurrent = true;
    }

    public void Start() => Dashboard.Start();

    /// <summary>Na primeira conexão com o serviço, reativa o controle (abrir o killer-sense = ativo).</summary>
    public void EnableOnFirstConnection()
    {
        var done = false;
        Dashboard.PropertyChanged += async (_, e) =>
        {
            if (done || e.PropertyName != nameof(DashboardViewModel.IsConnected) || !Dashboard.IsConnected)
            {
                return;
            }

            done = true;
            await SendQuietlyAsync(IpcRequestType.Enable).ConfigureAwait(true);
        };
    }

    /// <summary>"Encerrar killer-sense": desativa o controle no serviço (fans no Auto do EC).</summary>
    public Task DisableAsync() => SendQuietlyAsync(IpcRequestType.Disable);

    private async Task SendQuietlyAsync(IpcRequestType type)
    {
        try
        {
            await Dashboard.Client.SendCommandAsync(type, TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            // sem serviço: nada a fazer (o serviço sobe no Auto do EC)
        }
    }

    internal async Task NavigateAsync(NavItemViewModel item)
    {
        CurrentNav = item;
        if (item.Page == Settings && !Settings.IsDirty)
        {
            Settings.Reload(); // o atalho pode ter sido apagado por fora
        }

        if (item.Page == Fans && !Fans.IsLoaded && Dashboard.IsConnected)
        {
            await Fans.LoadAsync().ConfigureAwait(true);
        }

        if (item.Page == Gpu && !Gpu.IsDirty && Dashboard.IsConnected)
        {
            await Gpu.LoadAsync().ConfigureAwait(true);
        }

        if (item.Page == Lighting && !Lighting.IsDirty && Dashboard.IsConnected)
        {
            await Lighting.LoadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Depois de aplicar um perfil, as telas com configuração do serviço recarregam (se não houver edição pendente).</summary>
    private async void OnProfileApplied()
    {
        if (!Fans.IsDirty)
        {
            Fans.IsLoaded = false;
            if (CurrentNav.Page == Fans)
            {
                await Fans.LoadAsync().ConfigureAwait(true);
            }
        }

        if (CurrentNav.Page == Gpu && !Gpu.IsDirty)
        {
            await Gpu.LoadAsync().ConfigureAwait(true);
        }

        if (CurrentNav.Page == Lighting && !Lighting.IsDirty)
        {
            await Lighting.LoadAsync().ConfigureAwait(true);
        }
    }

    public ValueTask DisposeAsync() => Dashboard.DisposeAsync();
}

/// <summary>Um item do menu lateral.</summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    private readonly MainViewModel _owner;

    public NavItemViewModel(MainViewModel owner, string title, ObservableObject page, bool isPreview = false)
    {
        _owner = owner;
        Title = title;
        Page = page;
        IsPreview = isPreview;
    }

    public string Title { get; }

    public ObservableObject Page { get; }

    /// <summary>Tela pronta só no visual, ainda sem falar com o serviço.</summary>
    public bool IsPreview { get; }

    [ObservableProperty] public partial bool IsCurrent { get; set; }

    [RelayCommand]
    private Task SelectAsync() => _owner.NavigateAsync(this);
}
