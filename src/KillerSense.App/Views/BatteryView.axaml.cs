using Avalonia;
using Avalonia.Controls;
using KillerSense.App.ViewModels;

namespace KillerSense.App.Views;

public sealed partial class BatteryView : UserControl
{
    public BatteryView() => InitializeComponent();

    // A leitura da bateria só roda com a tela visível: trocar de página ou fechar a janela para.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as BatteryViewModel)?.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as BatteryViewModel)?.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
