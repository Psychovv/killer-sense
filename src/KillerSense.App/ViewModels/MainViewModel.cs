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
        CurrentPage = Dashboard;
    }

    public DashboardViewModel Dashboard { get; }

    public FansViewModel Fans { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboard), nameof(IsFans), nameof(Title))]
    public partial ObservableObject CurrentPage { get; set; }

    public bool IsDashboard => CurrentPage == Dashboard;

    public bool IsFans => CurrentPage == Fans;

    public string Title => IsFans ? "Fans" : "Painel";

    public void Start() => Dashboard.Start();

    [RelayCommand]
    private void ShowDashboard() => CurrentPage = Dashboard;

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
