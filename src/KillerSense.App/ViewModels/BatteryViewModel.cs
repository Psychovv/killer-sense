using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KillerSense.App.ViewModels;

/// <summary>
/// Tela Bateria (PRÉVIA, sem integração): estado da bateria e o limite de carga de 80% ("Battery Boundary
/// Charging" do Acer Care Center, classe <c>BatteryControl</c>; formato ainda a capturar no teste A8).
/// Só a fonte de energia é real (vem da telemetria do serviço); os outros números são de exemplo.
/// </summary>
public sealed partial class BatteryViewModel : ObservableObject
{
    private readonly DashboardViewModel _dashboard;

    public BatteryViewModel(DashboardViewModel dashboard)
    {
        _dashboard = dashboard;
        _dashboard.PropertyChanged += OnDashboardChanged;
        UpdatePower();
    }

    // Valores de exemplo até a integração (Windows: GetSystemPowerStatus / IOCTL da bateria).
    [ObservableProperty] public partial double ChargePercent { get; set; } = 78;
    [ObservableProperty] public partial bool IsCharging { get; set; } = true;
    [ObservableProperty] public partial double HealthPercent { get; set; } = 96;
    [ObservableProperty] public partial int CycleCount { get; set; } = 42;
    [ObservableProperty] public partial double FullCapacityWh { get; set; } = 86.4;
    [ObservableProperty] public partial double DesignCapacityWh { get; set; } = 90;

    [ObservableProperty] public partial string PowerSourceText { get; set; } = "—";
    [ObservableProperty] public partial string ChargeStateText { get; set; } = "—";

    /// <summary>Limite de carga em 80% (o firmware só oferece esse valor, como no Care Center).</summary>
    [ObservableProperty] public partial bool LimitCharge { get; set; } = true;

    [ObservableProperty] public partial string? Status { get; set; }

    partial void OnLimitChargeChanged(bool value)
    {
        Status = value
            ? "Prévia: limite de 80% marcado, mas nada foi enviado ao notebook."
            : "Prévia: limite desligado, mas nada foi enviado ao notebook.";
        UpdatePower();
    }

    partial void OnIsChargingChanged(bool value) => UpdatePower();

    private void OnDashboardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.OnAc) or nameof(DashboardViewModel.IsConnected))
        {
            UpdatePower();
        }
    }

    private void UpdatePower()
    {
        PowerSourceText = _dashboard.OnAc switch
        {
            true => "Na tomada",
            false => "Na bateria",
            null => "Desconhecida",
        };
        ChargeStateText = _dashboard.OnAc switch
        {
            false => "Descarregando",
            _ when LimitCharge && ChargePercent >= 80 => "Mantida em 80% (limite ligado)",
            _ when IsCharging => "Carregando",
            _ => "Carga completa",
        };
    }
}
