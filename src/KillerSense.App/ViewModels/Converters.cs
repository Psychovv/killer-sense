using Avalonia.Data.Converters;
using Avalonia.Media;

namespace KillerSense.App.ViewModels;

/// <summary>Conversores usados no XAML.</summary>
public static class Converters
{
    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#FF6B7A"));
    private static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#9A9AA6"));

    /// <summary>true ⇒ vermelho de erro; false ⇒ cinza de rótulo.</summary>
    public static IValueConverter ErrorBrush { get; } =
        new FuncValueConverter<bool, IBrush>(isError => isError ? Error : Normal);
}
