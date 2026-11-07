using Avalonia;
using Avalonia.Controls;

namespace KillerSense.App.Views;

public sealed partial class SettingsView : UserControl
{
    // Abaixo disto (largura útil da página) os cartões ficam apertados em duas colunas: empilha.
    private const double SingleColumnBelow = 760;

    private bool? _single;

    public SettingsView()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    private void Arrange(double width)
    {
        var single = width < SingleColumnBelow;
        if (_single == single)
        {
            return;
        }

        _single = single;
        Columns.ColumnDefinitions = single ? new ColumnDefinitions("*") : new ColumnDefinitions("*,16,*");
        Grid.SetColumn(RightColumn, single ? 0 : 2);
        Grid.SetRow(RightColumn, single ? 1 : 0);
        RightColumn.Margin = single ? new Thickness(0, 16, 0, 0) : default;
    }
}
