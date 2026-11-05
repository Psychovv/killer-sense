using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KillerSense.App.Platform;
using KillerSense.App.Settings;
using KillerSense.Core;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Painel ao vivo da tela Modos: CPU (contadores do Windows, lidos aqui só com a tela aberta) e GPU
/// (NVML, lida pelo serviço só com a GPU ligada). Só exibição: nada aqui muda o hardware.
/// </summary>
public sealed partial class ModeStatsViewModel : ObservableObject
{
    /// <summary>Clock máximo do i7-14700HX (5,5 GHz), escala do mostrador.</summary>
    public const double CpuMaxClockMhz = 5500;

    /// <summary>Clock máximo da RTX 4070 Laptop informado pelo driver (3105 MHz), escala do mostrador.</summary>
    public const double GpuMaxClockMhz = 3105;

    private static readonly NumberFormatInfo Pt = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    // PL1 dinâmico medido no teste A18 (na tomada). Na bateria ainda não foi medido.
    private static readonly Dictionary<OperatingMode, int> SustainedCpuLimitOnAc = new()
    {
        [OperatingMode.Quiet] = 55,
        [OperatingMode.Balanced] = 85,
        [OperatingMode.Performance] = 95,
        [OperatingMode.Turbo] = 115,
    };

    private readonly DashboardViewModel _dashboard;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CpuCounters? _cpu;

    public ModeStatsViewModel(DashboardViewModel dashboard)
    {
        _dashboard = dashboard;
        _dashboard.PropertyChanged += OnDashboardChanged;
        _timer.Tick += (_, _) => SampleCpu();
        UpdateGpu();
        UpdateLimit();
    }

    /// <summary>Limite sustentado da CPU (PL1) no modo, em W, medido no A18; null = não medido.</summary>
    public static int? SustainedCpuLimit(OperatingMode mode, bool? onAc) =>
        onAc == true && SustainedCpuLimitOnAc.TryGetValue(mode, out var w) ? w : null;

    // CPU
    [ObservableProperty] public partial double? CpuClock { get; set; }
    [ObservableProperty] public partial string CpuClockText { get; set; } = "—";
    [ObservableProperty] public partial string CpuUsageText { get; set; } = "—";
    [ObservableProperty] public partial string CpuPowerText { get; set; } = "—";
    [ObservableProperty] public partial string CpuLimitText { get; set; } = "—";
    [ObservableProperty] public partial string CpuTemperatureText { get; set; } = "—";

    // GPU
    [ObservableProperty] public partial bool GpuAwake { get; set; }
    [ObservableProperty] public partial double? GpuClock { get; set; }
    [ObservableProperty] public partial string GpuClockText { get; set; } = "—";
    [ObservableProperty] public partial string GpuMemoryClockText { get; set; } = "—";
    [ObservableProperty] public partial string GpuUsageText { get; set; } = "—";
    [ObservableProperty] public partial string GpuPowerText { get; set; } = "—";
    [ObservableProperty] public partial string GpuTemperatureText { get; set; } = "—";
    [ObservableProperty] public partial string GpuStateText { get; set; } = "—";

    /// <summary>A tela apareceu: começa a ler os contadores da CPU.</summary>
    public void Start()
    {
        _cpu ??= new CpuCounters();
        SampleCpu();
        _timer.Start();
    }

    /// <summary>A tela saiu: para de ler e libera os contadores.</summary>
    public void Stop()
    {
        _timer.Stop();
        _cpu?.Dispose();
        _cpu = null;
    }

    private void SampleCpu()
    {
        var s = _cpu?.Sample();
        CpuClock = s?.ClockMhz;
        CpuClockText = s?.ClockMhz is { } c ? c.ToString("0", Pt) : "—";
        CpuUsageText = s?.UtilizationPercent is { } u ? $"{u:0}%" : "—";
        CpuPowerText = s?.PackageWatts is { } w ? w.ToString("0", Pt) + " W" : "—";
        CpuTemperatureText = Temperatures.Text(_dashboard.CpuTemperature);
    }

    private void OnDashboardChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DashboardViewModel.GpuStats) or nameof(DashboardViewModel.GpuTemperature) or nameof(DashboardViewModel.IsConnected):
                UpdateGpu();
                break;
            case nameof(DashboardViewModel.CurrentMode) or nameof(DashboardViewModel.OnAc):
                UpdateLimit();
                break;
        }
    }

    private void UpdateLimit()
    {
        var mode = _dashboard.CurrentMode;
        CpuLimitText = mode is { } m && SustainedCpuLimit(m, _dashboard.OnAc) is { } w ? $"{w} W"
            : _dashboard.OnAc == false ? "não medido na bateria"
            : "—";
    }

    private void UpdateGpu()
    {
        var g = _dashboard.GpuStats;
        GpuAwake = g is not null;
        GpuClock = g?.CoreClockMhz;
        GpuClockText = g?.CoreClockMhz is { } c ? c.ToString("0", Pt) : "—";
        GpuMemoryClockText = g?.MemoryClockMhz is { } m ? m.ToString("N0", Pt) + " MHz" : "—";
        GpuUsageText = g?.UtilizationPercent is { } u ? $"{u}%" : "—";
        GpuPowerText = g?.PowerWatts is { } w
            ? w.ToString("0.0", Pt) + " W" + (g.PowerLimitWatts is { } l ? $" de {l:0} W" : "")
            : "—";
        GpuTemperatureText = Temperatures.Text(_dashboard.GpuTemperature);
        GpuStateText = g?.PerformanceState is { } p ? $"P{p}" : "—";
        if (!_dashboard.IsConnected)
        {
            GpuStateText = "serviço indisponível";
        }
    }
}
