using Avalonia;
using Avalonia.Controls;
using KillerSense.App.ViewModels;

namespace KillerSense.App.Views;

public sealed partial class ModesView : UserControl
{
    public ModesView() => InitializeComponent();

    // Os contadores da CPU só rodam com a tela visível: trocar de página ou fechar a janela para.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as ModesViewModel)?.Stats.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as ModesViewModel)?.Stats.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}
