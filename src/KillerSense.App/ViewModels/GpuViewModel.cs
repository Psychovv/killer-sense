using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela GPU: offset de clock (núcleo e memória) por modo, aplicado pelo serviço na troca de modo, como o
/// PredatorSense faz. Vem DESLIGADO; o padrão da Acer (PHN16-72) só acelera o Turbo (+100 / +200 MHz).
/// A validação final e os limites são sempre do serviço (<see cref="GpuOffsetPolicy"/>).
/// </summary>
public sealed partial class GpuViewModel : ObservableObject
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly ServiceClient? _client;
    private readonly DashboardViewModel? _dashboard;
    private bool _applying;

    /// <summary>Só para o designer/prévia.</summary>
    public GpuViewModel()
        : this(null, null)
    {
    }

    public GpuViewModel(ServiceClient? client, DashboardViewModel? dashboard)
    {
        _client = client;
        _dashboard = dashboard;
        Rows =
        [
            new(this, OperatingMode.Quiet, "Silencioso"),
            new(this, OperatingMode.Balanced, "Equilibrado"),
            new(this, OperatingMode.Performance, "Desempenho"),
            new(this, OperatingMode.Turbo, "Turbo"),
        ];
        if (_dashboard is not null)
        {
            _dashboard.PropertyChanged += OnDashboardChanged;
        }

        IsDirty = false;
        UpdateLive();
    }

    public IReadOnlyList<GpuOffsetRowViewModel> Rows { get; }

    public static string LimitsText { get; } =
        $"Limites do killer-sense: núcleo de {GpuOffsetPolicy.CoreMinMhz} a +{GpuOffsetPolicy.CoreMaxMhz} MHz · memória de {GpuOffsetPolicy.MemoryMinMhz} a +{GpuOffsetPolicy.MemoryMaxMhz} MHz.";

    public static decimal CoreMin => GpuOffsetPolicy.CoreMinMhz;

    public static decimal CoreMax => GpuOffsetPolicy.CoreMaxMhz;

    public static decimal MemoryMin => GpuOffsetPolicy.MemoryMinMhz;

    public static decimal MemoryMax => GpuOffsetPolicy.MemoryMaxMhz;

    [ObservableProperty] public partial bool Enabled { get; set; }
    [ObservableProperty] public partial bool IsLoaded { get; set; }
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool StatusIsError { get; set; }

    // Ao vivo (estado do serviço).
    [ObservableProperty] public partial string DriverText { get; set; } = "—";
    [ObservableProperty] public partial string TargetText { get; set; } = "—";
    [ObservableProperty] public partial string? LiveNote { get; set; }
    [ObservableProperty] public partial bool LiveNoteIsError { get; set; }

    partial void OnEnabledChanged(bool value) => Changed();

    internal void Changed()
    {
        if (_applying)
        {
            return;
        }

        IsDirty = true;
        Status = null;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            var reply = await _client.RequestAsync(new IpcRequest(IpcRequestType.GetGpuOffsets), Timeout).ConfigureAwait(true);
            if (reply.GpuOffsets?.TryToSettings(out var error) is { } settings)
            {
                Apply(settings);
                IsLoaded = true;
                SetStatus(null, false);
            }
            else
            {
                SetStatus(reply.Error ?? "O serviço não devolveu a configuração da GPU (versão antiga instalada?).", true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            SetStatus($"Não foi possível carregar: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private void RestoreAcerDefaults()
    {
        foreach (var row in Rows)
        {
            row.Core = row.AcerCore;
            row.Memory = row.AcerMemory;
        }

        SetStatus("Valores padrão da Acer restaurados (ainda não salvos).", false);
    }

    [RelayCommand]
    private Task DiscardAsync() => LoadAsync();

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_client is null)
        {
            return;
        }

        var dto = new GpuOffsetSettingsDto(Enabled, [.. Rows.Select(r => new GpuModeOffsetDto(r.ModeId, (int)r.Core, (int)r.Memory))]);
        if (dto.TryToSettings(out var error) is null)
        {
            SetStatus(error, true);
            return;
        }

        try
        {
            var ack = await _client.RequestAsync(new IpcRequest(IpcRequestType.SetGpuOffsets, GpuOffsets: dto), Timeout).ConfigureAwait(true);
            if (ack.Ok == true)
            {
                IsDirty = false;
                SetStatus(Enabled
                    ? "Salvo. O serviço aplica o offset do modo atual quando a GPU estiver ligada."
                    : "Salvo. Desligado: o serviço zera o que tiver gravado e não mexe mais no offset.", false);
            }
            else
            {
                SetStatus(ack.Error ?? "O serviço recusou a configuração.", true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            SetStatus($"Não foi possível salvar: {ex.Message}", true);
        }
    }

    private void Apply(GpuOffsetSettings settings)
    {
        _applying = true;
        try
        {
            Enabled = settings.Enabled;
            foreach (var row in Rows)
            {
                var o = settings.For(row.ModeId);
                row.Core = o.CoreMhz;
                row.Memory = o.MemoryMhz;
            }
        }
        finally
        {
            _applying = false;
        }

        IsDirty = false;
    }

    private void SetStatus(string? text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }

    private void OnDashboardChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DashboardViewModel.GpuOffset):
            case nameof(DashboardViewModel.CurrentMode):
            case nameof(DashboardViewModel.GpuStats):
                UpdateLive();
                break;
            case nameof(DashboardViewModel.IsConnected) when _dashboard!.IsConnected && !IsLoaded:
                _ = LoadAsync();
                break;
        }
    }

    private void UpdateLive()
    {
        var mode = _dashboard?.CurrentMode;
        foreach (var row in Rows)
        {
            row.IsCurrent = row.ModeId == mode;
        }

        if (_dashboard?.GpuOffset is not { } s)
        {
            DriverText = "—";
            TargetText = "—";
            LiveNote = _dashboard?.IsConnected == true ? "O serviço instalado ainda não tem o controle de offset." : null;
            LiveNoteIsError = false;
            return;
        }

        DriverText = s.Current is { } c ? Format(c) : "ainda não lido";
        TargetText = !s.Enabled ? "desligado: não mexe"
            : s.Target is { } t ? Format(t)
            : "zero (nada a gravar)";
        (LiveNote, LiveNoteIsError) = s switch
        {
            { Message: { } m } => ($"Erro do driver: {m}. O serviço tenta de novo em 1 min.", true),
            { Hot: true } => ($"GPU quente (≥ {GpuOffsetController.HotCelsius:0} °C) ou leitura inválida: offset retirado até esfriar abaixo de {GpuOffsetController.CoolCelsius:0} °C.", true),
            { Pending: true, Enabled: true } when _dashboard.GpuStats is null => ("GPU dormindo: o offset é aplicado quando ela ligar (o killer-sense não a acorda).", false),
            _ => (null, false),
        };
    }

    private static string Format(GpuClockOffset o) =>
        $"núcleo {o.CoreMhz:+0;-0;0} MHz · memória {o.MemoryMhz:+0;-0;0} MHz";
}

/// <summary>Offsets de um modo.</summary>
public sealed partial class GpuOffsetRowViewModel : ObservableObject
{
    private readonly GpuViewModel _owner;

    public GpuOffsetRowViewModel(GpuViewModel owner, OperatingMode mode, string name)
    {
        _owner = owner;
        ModeId = mode;
        Mode = name;
        var acer = GpuOffsetSettings.AcerDefaults.TryGetValue(mode, out var o) ? o : GpuClockOffset.Zero;
        AcerCore = acer.CoreMhz;
        AcerMemory = acer.MemoryMhz;
        Core = AcerCore;
        Memory = AcerMemory;
    }

    public OperatingMode ModeId { get; }

    public string Mode { get; }

    public decimal AcerCore { get; }

    public decimal AcerMemory { get; }

    [ObservableProperty] public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAcerDefault))]
    public partial decimal Core { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAcerDefault))]
    public partial decimal Memory { get; set; }

    public bool IsAcerDefault => Core == AcerCore && Memory == AcerMemory;

    partial void OnCoreChanged(decimal value) => _owner.Changed();

    partial void OnMemoryChanged(decimal value) => _owner.Changed();
}
