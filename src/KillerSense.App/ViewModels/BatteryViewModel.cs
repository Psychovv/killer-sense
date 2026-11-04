using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KillerSense.App.Platform;
using KillerSense.Ipc;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Bateria: carga, saúde e potência lidas do driver da bateria do Windows (no app, sem admin) e o
/// limite de carga de 80% ("Battery Boundary Charging" do Acer Care Center, <c>BatteryControl</c>, teste A8),
/// que passa pelo serviço. Não "segura" o limite: se o Care Center mudar, a tela só acompanha.
/// </summary>
public sealed partial class BatteryViewModel : ObservableObject
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    // Globalização invariante (mais leve): formato brasileiro feito à mão, como no Painel.
    private static readonly System.Globalization.NumberFormatInfo Pt = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };
    private readonly ServiceClient _client;
    private readonly DashboardViewModel _dashboard;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _applying;
    private bool _sending;

    public BatteryViewModel(ServiceClient client, DashboardViewModel dashboard)
    {
        _client = client;
        _dashboard = dashboard;
        _dashboard.PropertyChanged += OnDashboardChanged;
        _timer.Tick += (_, _) => ReadBattery();
        _timer.Start();
        ReadBattery();
        ApplyLimitFromService();
    }

    [ObservableProperty] public partial double ChargePercent { get; set; }
    [ObservableProperty] public partial bool IsCharging { get; set; }
    [ObservableProperty] public partial double? HealthPercent { get; set; }
    [ObservableProperty] public partial int? CycleCount { get; set; }
    [ObservableProperty] public partial double? FullCapacityWh { get; set; }
    [ObservableProperty] public partial double? DesignCapacityWh { get; set; }
    [ObservableProperty] public partial string RateText { get; set; } = "—";
    [ObservableProperty] public partial string FullCapacityText { get; set; } = "—";
    [ObservableProperty] public partial string DesignCapacityText { get; set; } = "—";
    [ObservableProperty] public partial bool HasBattery { get; set; }

    [ObservableProperty] public partial string PowerSourceText { get; set; } = "—";
    [ObservableProperty] public partial string ChargeStateText { get; set; } = "—";

    /// <summary>Limite de carga em 80% (o firmware só oferece esse valor, como no Care Center).</summary>
    [ObservableProperty] public partial bool LimitCharge { get; set; }

    /// <summary>O estado do limite é conhecido e dá para trocar agora.</summary>
    [ObservableProperty] public partial bool CanChangeLimit { get; set; }
    [ObservableProperty] public partial string? Notice { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool StatusIsError { get; set; }

    partial void OnLimitChargeChanged(bool value)
    {
        UpdateTexts();
        if (!_applying)
        {
            _ = SendLimitAsync(value);
        }
    }

    private async Task SendLimitAsync(bool enabled)
    {
        _sending = true;
        CanChangeLimit = false;
        SetStatus(enabled ? "Ligando o limite…" : "Desligando o limite…", false);
        try
        {
            var ack = await _client.RequestAsync(new IpcRequest(IpcRequestType.SetChargeLimit, Enabled: enabled), Timeout).ConfigureAwait(true);
            SetStatus(ack.Ok == true ? (enabled ? "Limite de 80% ligado." : "Limite de 80% desligado.") : ack.Error ?? "O serviço recusou a troca.", ack.Ok != true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            SetStatus($"Não foi possível trocar o limite: {ex.Message}", true);
        }
        finally
        {
            _sending = false;
        }

        ApplyLimitFromService(); // volta ao estado real se falhou
    }

    private void OnDashboardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.ChargeLimitOn) or nameof(DashboardViewModel.IsConnected)
            or nameof(DashboardViewModel.IsDisabled) or nameof(DashboardViewModel.OnAc))
        {
            ApplyLimitFromService();
        }
    }

    private void ApplyLimitFromService()
    {
        if (_sending)
        {
            return; // o resultado do pedido em andamento decide
        }

        var known = _dashboard.ChargeLimitOn;
        _applying = true;
        try
        {
            if (known is { } on)
            {
                LimitCharge = on;
            }
        }
        finally
        {
            _applying = false;
        }

        CanChangeLimit = _dashboard.IsConnected && !_dashboard.IsDisabled && known is not null;
        Notice = !_dashboard.IsConnected ? "Serviço killer-sense indisponível: o limite não pode ser lido nem trocado."
            : _dashboard.IsDisabled ? "killer-sense desativado: o Acer Care Center está no controle do limite."
            : known is null ? "Lendo o limite de carga no firmware…"
            : null;
        UpdateTexts();
    }

    private void ReadBattery()
    {
        var r = WindowsBattery.Read();
        HasBattery = r is not null;
        if (r is null)
        {
            return;
        }

        ChargePercent = Math.Round(r.Percent);
        IsCharging = r.Charging;
        FullCapacityWh = r.FullChargedMwh / 1000;
        DesignCapacityWh = r.DesignMwh / 1000;
        HealthPercent = r.HealthPercent is { } h ? Math.Round(h) : null;
        CycleCount = r.CycleCount;
        FullCapacityText = Wh(FullCapacityWh);
        DesignCapacityText = Wh(DesignCapacityWh);
        RateText = r.RateMw switch
        {
            null or 0 => "—",
            > 0 and var w => "+" + (w / 1000).ToString("0.0", Pt) + " W",
            double w => (w / 1000).ToString("0.0", Pt) + " W",
        };
        UpdateTexts(r.OnLine);
    }

    private void UpdateTexts(bool? onLine = null)
    {
        var ac = onLine ?? _dashboard.OnAc;
        PowerSourceText = ac switch
        {
            true => "Na tomada",
            false => "Na bateria",
            null => "Desconhecida",
        };
        ChargeStateText = ac switch
        {
            false => "Descarregando",
            _ when IsCharging => "Carregando",
            _ when LimitCharge && _dashboard.ChargeLimitOn == true => "Sem carregar (limite de 80% ligado)",
            _ when ChargePercent >= 99 => "Carga completa",
            _ => "Sem carregar",
        };
    }

    private static string Wh(double? value) => value is { } v ? v.ToString("0.0", Pt) + " Wh" : "—";

    private void SetStatus(string? text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }
}
