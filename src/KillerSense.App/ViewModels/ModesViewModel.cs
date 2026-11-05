using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Modos: mostra o modo real (lido do firmware pelo serviço) e troca a pedido. Não "segura" o modo:
/// se o PredatorSense ou a tecla mudarem depois, a tela só acompanha (decisão do usuário, 2026-11-06).
/// </summary>
public sealed partial class ModesViewModel : ObservableObject
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly ServiceClient _client;
    private readonly DashboardViewModel _dashboard;

    public ModesViewModel(ServiceClient client, DashboardViewModel dashboard)
    {
        _client = client;
        _dashboard = dashboard;
        Stats = new ModeStatsViewModel(dashboard);
        // Limites medidos no teste A18 (na tomada): o modo muda só o limite sustentado da CPU (PL1);
        // pico curto (PL2) de 157 W e limites da GPU iguais em todos.
        Cards =
        [
            new(this, OperatingMode.Quiet, "Silencioso", "CPU sustentada até 55 W: menos calor e barulho em carga longa. Só na tomada."),
            new(this, OperatingMode.Balanced, "Equilibrado", "CPU sustentada até 85 W. O padrão do notebook. Na tomada e na bateria."),
            new(this, OperatingMode.Performance, "Desempenho", "CPU sustentada até 95 W. Só na tomada; fan da CPU com mínimo de 15% na curva."),
            new(this, OperatingMode.Turbo, "Turbo", "CPU sustentada até 115 W, o máximo. Só na tomada; fan da CPU com mínimo de 30% na curva. Sem o overclock de GPU do PredatorSense."),
            new(this, OperatingMode.Eco, "Eco", "Economia de bateria (limites ainda não medidos). Só na bateria."),
        ];
        _dashboard.PropertyChanged += OnDashboardChanged;
        Refresh();
    }

    public IReadOnlyList<ModeCardViewModel> Cards { get; }

    /// <summary>Painel ao vivo de CPU e GPU abaixo dos cartões.</summary>
    public ModeStatsViewModel Stats { get; }

    [ObservableProperty] public partial string PowerText { get; set; } = "";
    [ObservableProperty] public partial string? Notice { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool StatusIsError { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    /// <summary>Dá para trocar agora: conectado, ativo, fonte de energia conhecida e nada em andamento.</summary>
    public bool CanChange => _dashboard.IsConnected && !_dashboard.IsDisabled && _dashboard.OnAc is not null && !IsBusy;

    partial void OnIsBusyChanged(bool value) => Refresh();

    internal async Task SelectAsync(ModeCardViewModel card)
    {
        if (!CanChange || card.IsCurrent)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var ack = await _client.RequestAsync(new IpcRequest(IpcRequestType.SetOperatingMode, Mode: card.Mode), Timeout).ConfigureAwait(true);
            if (ack.Ok == true)
            {
                SetStatus($"Modo {card.Title} aplicado.", false);
            }
            else
            {
                SetStatus(ack.Error ?? "O serviço recusou a troca de modo.", true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            SetStatus($"Não foi possível trocar o modo: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnDashboardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.CurrentMode) or nameof(DashboardViewModel.OnAc)
            or nameof(DashboardViewModel.IsConnected) or nameof(DashboardViewModel.IsDisabled))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var onAc = _dashboard.OnAc;
        var available = onAc is { } ac ? OperatingModes.Available(ac) : [];
        foreach (var card in Cards)
        {
            card.IsCurrent = _dashboard.CurrentMode == card.Mode;
            // O modo atual sempre aparece (ex.: Turbo logo depois de tirar da tomada).
            card.IsVisible = available.Contains(card.Mode) || card.IsCurrent;
            card.IsAvailable = available.Contains(card.Mode);
        }

        PowerText = onAc switch
        {
            true => "Na tomada: Silencioso, Equilibrado, Desempenho e Turbo.",
            false => "Na bateria: Equilibrado e Eco.",
            null => "Fonte de energia desconhecida.",
        };
        Notice = !_dashboard.IsConnected ? "Serviço killer-sense indisponível."
            : _dashboard.IsDisabled ? "killer-sense desativado: o PredatorSense está no controle. Clique em Retomar para trocar o modo por aqui."
            : null;
        OnPropertyChanged(nameof(CanChange));
        foreach (var card in Cards)
        {
            card.SelectCommand.NotifyCanExecuteChanged();
        }
    }

    private void SetStatus(string? text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }
}

/// <summary>Um cartão de modo na tela.</summary>
public sealed partial class ModeCardViewModel : ObservableObject
{
    private readonly ModesViewModel _owner;

    public ModeCardViewModel(ModesViewModel owner, OperatingMode mode, string title, string description)
    {
        _owner = owner;
        Mode = mode;
        Title = title;
        Description = description;
    }

    public OperatingMode Mode { get; }

    public string Title { get; }

    public string Description { get; }

    [ObservableProperty] public partial bool IsCurrent { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; }
    [ObservableProperty] public partial bool IsAvailable { get; set; }

    private bool CanSelect() => _owner.CanChange && IsAvailable && !IsCurrent;

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private Task SelectAsync() => _owner.SelectAsync(this);
}
