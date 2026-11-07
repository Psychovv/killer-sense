using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KillerSense.App.Platform;
using KillerSense.Core;

namespace KillerSense.App.ViewModels;

/// <summary>Uma linha de "Programas que mais pesam".</summary>
public sealed record ProgramRow(string Name, string CpuText, string MemoryText);

/// <summary>
/// Dados ao vivo do Painel: uso, clock e potência da CPU (contadores do Windows) e da GPU (o serviço já manda),
/// a RAM e os programas que mais usam a CPU. CPU, RAM e programas só são lidos com o Painel aberto
/// (CPU a cada 1 s; RAM e programas a cada 2 s). Só exibição: nada aqui fala com o firmware.
/// </summary>
public sealed partial class LiveStatsViewModel : ObservableObject
{
    public const int TopCount = 3;
    private const double Gigabyte = 1024d * 1024 * 1024;
    private static readonly NumberFormatInfo Pt = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    private readonly DashboardViewModel _dashboard;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ProcessCpuTracker _programs = new();
    private readonly Stopwatch _programsClock = new();
    private CpuCounters? _cpu;
    private ProcessSnapshot? _snapshot;
    private int _ticks;

    public LiveStatsViewModel(DashboardViewModel dashboard)
    {
        _dashboard = dashboard;
        _dashboard.PropertyChanged += OnDashboardChanged;
        _timer.Tick += (_, _) => Sample();
        UpdateGpu();
    }

    // CPU
    [ObservableProperty] public partial string CpuUsageText { get; set; } = "—";
    [ObservableProperty] public partial string CpuClockText { get; set; } = "—";
    [ObservableProperty] public partial string CpuPowerText { get; set; } = "—";

    // GPU
    [ObservableProperty] public partial string GpuUsageText { get; set; } = "—";
    [ObservableProperty] public partial string GpuClockText { get; set; } = "—";
    [ObservableProperty] public partial string GpuPowerText { get; set; } = "—";

    // RAM
    [ObservableProperty] public partial string RamUsedText { get; set; } = "—";
    [ObservableProperty] public partial string RamTotalText { get; set; } = "";
    [ObservableProperty] public partial double RamPercent { get; set; }
    [ObservableProperty] public partial string RamPercentText { get; set; } = "";

    // Programas
    [ObservableProperty] public partial IReadOnlyList<ProgramRow> Programs { get; set; } = [];
    [ObservableProperty] public partial string? ProgramsStatus { get; set; } = "medindo…";

    /// <summary>O Painel apareceu: começa a ler.</summary>
    public void Start()
    {
        _cpu ??= new CpuCounters();
        _snapshot ??= new ProcessSnapshot();
        _ticks = 0;
        _programs.Update(_snapshot.Read(), 0, Environment.ProcessorCount, TopCount); // base para a próxima leitura
        _programsClock.Restart();
        Programs = [];
        ProgramsStatus = "medindo…";
        SampleCpu();
        SampleMemory();
        _timer.Start();
    }

    /// <summary>O Painel saiu (outra página ou janela fechada): para de ler e libera os contadores.</summary>
    public void Stop()
    {
        _timer.Stop();
        _cpu?.Dispose();
        _cpu = null;
        _snapshot?.Dispose();
        _snapshot = null;
        _dashboard.ReportCpuUsage(null);
    }

    private void Sample()
    {
        SampleCpu();
        if (++_ticks % 2 == 0)
        {
            SampleMemory();
            SamplePrograms();
        }
    }

    private void SampleCpu()
    {
        var s = _cpu?.Sample();
        CpuUsageText = s?.UtilizationPercent is { } u ? $"{u:0}%" : "—";
        CpuClockText = s?.ClockMhz is { } c ? c.ToString("N0", Pt) + " MHz" : "—";
        CpuPowerText = s?.PackageWatts is { } w ? w.ToString("0.0", Pt) + " W" : "—";
        _dashboard.ReportCpuUsage(s?.UtilizationPercent);
    }

    private void SampleMemory()
    {
        if (MemoryStatus.Read() is not { } m)
        {
            RamUsedText = "—";
            RamTotalText = "";
            RamPercent = 0;
            RamPercentText = "";
            return;
        }

        var used = m.Total - Math.Min(m.Available, m.Total);
        RamUsedText = Gb(used);
        RamTotalText = $"de {Gb(m.Total)} em uso";
        RamPercent = used * 100d / m.Total;
        RamPercentText = $"{RamPercent:0}% da memória";
    }

    private void SamplePrograms()
    {
        if (_snapshot is null)
        {
            return;
        }

        var elapsed = _programsClock.Elapsed.Ticks; // unidades de 100 ns
        _programsClock.Restart();
        var top = _programs.Update(_snapshot.Read(), elapsed, Environment.ProcessorCount, TopCount);
        Programs = [.. top.Select(ToRow)];
        ProgramsStatus = top.Count == 0 ? "medindo…" : null;
    }

    internal static ProgramRow ToRow(ProgramUsage p) => new(
        p.Processes > 1 ? $"{p.Name} ({p.Processes})" : p.Name,
        p.CpuPercent.ToString("0.0", Pt) + "%",
        p.PrivateBytes <= 0 ? "—" // ex.: System (memória do kernel não aparece aqui)
            : p.PrivateBytes >= Gigabyte ? Gb(p.PrivateBytes)
            : (p.PrivateBytes / (1024d * 1024)).ToString("N0", Pt) + " MB");

    private static string Gb(double bytes) => (bytes / Gigabyte).ToString("0.0", Pt) + " GB";

    private void OnDashboardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.GpuStats) or nameof(DashboardViewModel.IsConnected))
        {
            UpdateGpu();
        }
    }

    private void UpdateGpu()
    {
        var g = _dashboard.GpuStats;
        if (!_dashboard.IsConnected || g is null)
        {
            GpuUsageText = _dashboard.IsConnected ? "dormindo" : "—"; // sem dados = GPU desligada (não a acordamos)
            GpuClockText = "—";
            GpuPowerText = "—";
            return;
        }

        GpuUsageText = g.UtilizationPercent is { } u ? $"{u}%" : "—";
        GpuClockText = g.CoreClockMhz is { } c ? c.ToString("N0", Pt) + " MHz" : "—";
        GpuPowerText = g.PowerWatts is { } w
            ? w.ToString("0.0", Pt) + " W" + (g.PowerLimitWatts is { } l ? $" de {l:0} W" : "")
            : "—";
    }
}
