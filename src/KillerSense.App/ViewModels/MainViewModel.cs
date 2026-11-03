using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KillerSense.Ipc;

namespace KillerSense.App.ViewModels;

/// <summary>Janela principal: navegação entre as telas e a barra de estado comum.</summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    public MainViewModel(ServiceClient client)
    {
        Dashboard = new DashboardViewModel(client);
        Fans = new FansViewModel(client, Dashboard);
        Modes = new ModesViewModel(client, Dashboard);
        CurrentPage = Dashboard;
    }

    public DashboardViewModel Dashboard { get; }

    public FansViewModel Fans { get; }

    public ModesViewModel Modes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboard), nameof(IsFans), nameof(IsModes), nameof(Title))]
    public partial ObservableObject CurrentPage { get; set; }

    public bool IsDashboard => CurrentPage == Dashboard;

    public bool IsFans => CurrentPage == Fans;

    public bool IsModes => CurrentPage == Modes;

    public string Title => IsFans ? "Fans" : IsModes ? "Modos" : "Painel";

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

    [RelayCommand]
    private void ShowDashboard() => CurrentPage = Dashboard;

    [RelayCommand]
    private void ShowModes() => CurrentPage = Modes;

    [RelayCommand]
    private async Task ShowFansAsync()
    {
        CurrentPage = Fans;
        if (!Fans.IsLoaded && Dashboard.IsConnected)
        {
            await Fans.LoadAsync().ConfigureAwait(true);
        }
    }

    public ValueTask DisposeAsync() => Dashboard.DisposeAsync();
}
