using Avalonia.Data.Converters;
using Avalonia.Media;
using KillerSense.App.Settings;

namespace KillerSense.App.ViewModels;

/// <summary>Conversores usados no XAML.</summary>
public static class Converters
{
    /// <summary>true ⇒ vermelho de erro; false ⇒ cinza de rótulo (cores do tema atual).</summary>
    public static IValueConverter ErrorBrush { get; } =
        new FuncValueConverter<bool, IBrush>(isError => isError
            ? AppTheme.Brush("KsHotBrush", Brushes.IndianRed)
            : AppTheme.Brush("KsMutedBrush", Brushes.Gray));
}
