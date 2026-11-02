using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Fans: carrega o perfil do serviço, edita curvas e parâmetros e salva. A validação final é
/// sempre do serviço — aqui só se evita mandar algo obviamente errado.
/// </summary>
public sealed partial class FansViewModel : ObservableObject
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly ServiceClient _client;
    private readonly DashboardViewModel _dashboard;
    private bool _applying;

    public FansViewModel(ServiceClient client, DashboardViewModel dashboard)
    {
        _client = client;
        _dashboard = dashboard;
        _dashboard.PropertyChanged += OnDashboardChanged;
    }

    [ObservableProperty] public partial IReadOnlyList<CurvePoint>? CpuCurve { get; set; }
    [ObservableProperty] public partial IReadOnlyList<CurvePoint>? GpuCurve { get; set; }
    [ObservableProperty] public partial double CpuEntryTemperature { get; set; } = 60;
    [ObservableProperty] public partial double GpuEntryTemperature { get; set; } = 55;
    [ObservableProperty] public partial decimal Hysteresis { get; set; } = 3;
    [ObservableProperty] public partial decimal RampUp { get; set; } = 15;
    [ObservableProperty] public partial decimal RampDown { get; set; } = 3;
    [ObservableProperty] public partial bool IsLoaded { get; set; }
    [ObservableProperty] public partial bool IsDirty { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool StatusIsError { get; set; }
    [ObservableProperty] public partial double? CpuNow { get; set; }
    [ObservableProperty] public partial double? GpuNow { get; set; }
    [ObservableProperty] public partial double Floor { get; set; }
    [ObservableProperty] public partial string FloorText { get; set; } = "";

    partial void OnCpuCurveChanged(IReadOnlyList<CurvePoint>? value) => MarkDirty();

    partial void OnGpuCurveChanged(IReadOnlyList<CurvePoint>? value) => MarkDirty();

    partial void OnCpuEntryTemperatureChanged(double value) => MarkDirty();

    partial void OnGpuEntryTemperatureChanged(double value) => MarkDirty();

    partial void OnHysteresisChanged(decimal value) => MarkDirty();

    partial void OnRampUpChanged(decimal value) => MarkDirty();

    partial void OnRampDownChanged(decimal value) => MarkDirty();

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var reply = await _client.RequestAsync(new IpcRequest(IpcRequestType.GetProfile), Timeout).ConfigureAwait(true);
            if (reply.Profile is { } p)
            {
                Apply(p);
                IsLoaded = true;
                SetStatus(null, false);
            }
            else
            {
                SetStatus(reply.Error ?? "O serviço não devolveu o perfil.", true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            SetStatus($"Não foi possível carregar o perfil: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (CpuCurve is null || GpuCurve is null)
        {
            return;
        }

        var dto = new FanProfileDto(
            [.. CpuCurve.Select(p => new CurvePointDto(p.Temperature, p.Percent))],
            [.. GpuCurve.Select(p => new CurvePointDto(p.Temperature, p.Percent))],
            Math.Round(CpuEntryTemperature),
            Math.Round(GpuEntryTemperature),
            (double)Hysteresis,
            (double)RampUp,
            (double)RampDown);
        if (dto.TryToProfile(out var error) is null)
        {
            SetStatus(error, true);
            return;
        }

        try
        {
            var ack = await _client.RequestAsync(new IpcRequest(IpcRequestType.SetProfile, dto), Timeout).ConfigureAwait(true);
            if (ack.Ok == true)
            {
                IsDirty = false;
                SetStatus("Perfil salvo e aplicado.", false);
            }
            else
            {
                SetStatus(ack.Error ?? "O serviço recusou o perfil.", true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            SetStatus($"Não foi possível salvar: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private Task DiscardAsync() => LoadAsync();

    [RelayCommand]
    private void RestoreDefaults()
    {
        Apply(FanProfileDto.From(FanProfile.Default));
        IsDirty = true;
        SetStatus("Valores padrão carregados — salve para aplicar.", false);
    }

    private void Apply(FanProfileDto p)
    {
        _applying = true;
        try
        {
            CpuCurve = [.. p.CpuCurve.Select(x => new CurvePoint(x.Temperature, x.Percent))];
            GpuCurve = [.. p.GpuCurve.Select(x => new CurvePoint(x.Temperature, x.Percent))];
            CpuEntryTemperature = p.CpuEntryTemperature;
            GpuEntryTemperature = p.GpuEntryTemperature;
            Hysteresis = (decimal)p.HysteresisCelsius;
            RampUp = (decimal)p.RampUpPercentPerSecond;
            RampDown = (decimal)p.RampDownPercentPerSecond;
            IsDirty = false;
        }
        finally
        {
            _applying = false;
        }
    }

    private void MarkDirty()
    {
        if (!_applying)
        {
            IsDirty = true;
        }
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
            case nameof(DashboardViewModel.CpuTemperature):
                CpuNow = _dashboard.CpuTemperature;
                break;
            case nameof(DashboardViewModel.GpuTemperature):
                GpuNow = _dashboard.GpuTemperature;
                break;
            case nameof(DashboardViewModel.CurrentMode):
                var mode = _dashboard.CurrentMode ?? OperatingMode.Turbo;
                Floor = SafetyPolicy.FloorPercent(mode);
                FloorText = Floor > 0 ? $"Piso de segurança no modo atual: {Floor:0}%" : "Sem piso no modo atual (0%).";
                break;
            case nameof(DashboardViewModel.IsConnected) when _dashboard.IsConnected && !IsLoaded:
                _ = LoadAsync();
                break;
        }
    }
}
