using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using KillerSense.App.ViewModels;

namespace KillerSense.App.Views;

public sealed partial class DashboardView : UserControl
{
    // Medidor (190) + espaço (22) + "Rotação  4.166 rpm" (~180) + padding do cartão (36).
    // Abaixo disto os rótulos ficam cortados ao lado do medidor: as linhas vão para baixo dele.
    private const double SideBySideFrom = 430;

    private bool? _stacked;

    public DashboardView()
    {
        InitializeComponent();
        Cards.SizeChanged += (_, e) => Arrange((e.NewSize.Width - 16) / 2);
    }

    // Uso da CPU, RAM e programas só são lidos com o Painel visível: trocar de página ou fechar a janela para.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as DashboardViewModel)?.Live.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as DashboardViewModel)?.Live.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Arrange(double cardWidth)
    {
        var stacked = cardWidth < SideBySideFrom;
        if (_stacked == stacked)
        {
            return;
        }

        _stacked = stacked;
        Place(CpuLayout, CpuStats, stacked);
        Place(GpuLayout, GpuStats, stacked);
    }

    private static void Place(Grid layout, Grid stats, bool stacked)
    {
        layout.ColumnDefinitions = stacked ? new ColumnDefinitions("*") : new ColumnDefinitions("190,22,*");
        layout.RowDefinitions = stacked ? new RowDefinitions("Auto,Auto") : new RowDefinitions("*");
        Grid.SetRow(stats, stacked ? 1 : 0);
        Grid.SetColumn(stats, stacked ? 0 : 2);
        stats.Margin = stacked ? new Thickness(0, 14, 0, 0) : default;
        stats.VerticalAlignment = stacked ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (layout.Children[0] is Panel gauge)
        {
            gauge.HorizontalAlignment = stacked ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        }
    }
}
