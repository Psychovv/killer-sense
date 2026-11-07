using Avalonia.Controls;
using KillerSense.App.Settings;

namespace KillerSense.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppTheme.Mark(this); // classe "metal" quando o tema Metal está ativo
    }
}
