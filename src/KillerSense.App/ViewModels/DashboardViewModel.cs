using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KillerSense.App.Controls;
using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.App.ViewModels;

/// <summary>Painel: estado do serviço (via pipe), histórico de temperaturas e os comandos de segurança.</summary>
public sealed partial class DashboardViewModel : ObservableObject, IAsyncDisposable
{
    private const int HistoryCapacity = 120; // 2 min a 1 s
    // Globalização invariante (mais leve): formato brasileiro feito à mão.
    private static readonly NumberFormatInfo Pt = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };
    private static readonly IBrush CpuBrush = new SolidColorBrush(Color.Parse("#8B7BFF"));
    private static readonly IBrush GpuBrush = new SolidColorBrush(Color.Parse("#5EC8FF"));
    private readonly ServiceClient _client;
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _gpuHistory = new();

    public DashboardViewModel(ServiceClient client)
    {
        _client = client;
        _client.StateReceived += s => Dispatcher.UIThread.Post(() => Apply(s));
        _client.ConnectionChanged += c => Dispatcher.UIThread.Post(() => SetConnected(c));
        SetConnected(false);
    }

    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty] public partial string ConnectionText { get; set; } = "";
    [ObservableProperty] public partial double? CpuTemperature { get; set; }
    [ObservableProperty] public partial double? GpuTemperature { get; set; }
    [ObservableProperty] public partial string CpuTemperatureText { get; set; } = "—";
    [ObservableProperty] public partial string GpuTemperatureText { get; set; } = "—";
    [ObservableProperty] public partial string CpuFanText { get; set; } = "—";
    [ObservableProperty] public partial string CpuRpmText { get; set; } = "—";
    [ObservableProperty] public partial string GpuFanText { get; set; } = "—";
    [ObservableProperty] public partial string GpuRpmText { get; set; } = "—";
    [ObservableProperty] public partial string OperatingModeText { get; set; } = "—";
    [ObservableProperty] public partial string StateText { get; set; } = "—";
    [ObservableProperty] public partial string? StateDetail { get; set; }
    [ObservableProperty] public partial bool IsPaused { get; set; }
    [ObservableProperty] public partial string? CommandError { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ChartSeries>? History { get; set; }

    public void Start() => _client.Start();

    [RelayCommand]
    private Task RestoreAutoAsync() => SendAsync(IpcRequestType.RestoreAuto);

    [RelayCommand]
    private Task ResumeAsync() => SendAsync(IpcRequestType.Resume);

    private async Task SendAsync(IpcRequestType type)
    {
        try
        {
            var ack = await _client.SendCommandAsync(type, TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            CommandError = ack.Ok == true ? null : ack.Error ?? "O serviço recusou o comando.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            CommandError = ex.Message;
        }
    }

    private void SetConnected(bool connected)
    {
        IsConnected = connected;
        ConnectionText = connected ? "Conectado ao serviço" : "Serviço killer-sense indisponível — fans no controle do notebook";
        if (!connected)
        {
            StateText = "Sem conexão";
        }
    }

    private void Apply(ServiceState s)
    {
        CpuTemperature = s.CpuTemperature;
        GpuTemperature = s.GpuTemperature;
        CpuTemperatureText = s.CpuTemperature is { } c ? c.ToString("0", Pt) : "—";
        GpuTemperatureText = s.GpuTemperature is { } g ? g.ToString("0", Pt) : "—";
        CpuFanText = FanText(s.CpuFanMode, s.CpuFanPercent);
        GpuFanText = s.GpuTemperature is null && s.GpuFanMode == FanMode.Auto ? "Auto do EC (GPU desligada ou sem leitura)" : FanText(s.GpuFanMode, s.GpuFanPercent);
        CpuRpmText = Rpm(s.CpuFanRpm);
        GpuRpmText = Rpm(s.GpuFanRpm);
        OperatingModeText = s.OperatingMode switch
        {
            OperatingMode.Quiet => "Silencioso",
            OperatingMode.Balanced => "Equilibrado",
            OperatingMode.Performance => "Desempenho",
            OperatingMode.Turbo => "Turbo",
            OperatingMode.Eco => "Eco",
            _ => "—",
        };
        StateText = s.State switch
        {
            EngineState.Auto => "Fans no Auto do notebook",
            EngineState.Curve => "Curva ativa",
            EngineState.Emergency => "EMERGÊNCIA — fans no máximo",
            EngineState.SensorFault => "Falha de sensor — Auto",
            EngineState.ExternalOverride => "Outro programa mudou as fans",
            EngineState.FirmwareFault => "Erro no firmware — Auto",
            EngineState.Paused => "Pausado — Auto",
            _ => s.State.ToString(),
        };
        StateDetail = s.Message;
        IsPaused = s.State is EngineState.Paused or EngineState.ExternalOverride or EngineState.FirmwareFault;
        Push(_cpuHistory, s.CpuTemperature);
        Push(_gpuHistory, s.GpuTemperature);
        History = [new ChartSeries(_gpuHistory.ToArray(), GpuBrush), new ChartSeries(_cpuHistory.ToArray(), CpuBrush)];
    }

    private static string FanText(FanMode mode, int? percent) => mode switch
    {
        FanMode.Auto => "Auto do EC",
        FanMode.Max => "Máximo",
        FanMode.Custom => percent is { } p ? $"Curva · {p}%" : "Curva",
        _ => "—",
    };

    private static string Rpm(int? rpm) => rpm is { } r ? $"{r.ToString("N0", Pt)} rpm" : "—";

    private static void Push(Queue<double> q, double? v)
    {
        q.Enqueue(v ?? double.NaN);
        while (q.Count > HistoryCapacity)
        {
            q.Dequeue();
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
