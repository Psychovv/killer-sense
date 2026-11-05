using System.Globalization;

namespace KillerSense.App.Settings;

/// <summary>
/// Exibição de temperaturas na unidade escolhida. Tudo continua em °C por dentro (serviço, curva salva,
/// logs); só o texto mostrado muda.
/// </summary>
public static class Temperatures
{
    // Globalização invariante (mais leve): formato brasileiro feito à mão.
    private static readonly NumberFormatInfo Pt = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static bool Fahrenheit => AppSettings.Current.Fahrenheit;

    public static string Unit => Fahrenheit ? "°F" : "°C";

    /// <summary>Converte uma temperatura em °C para a unidade escolhida.</summary>
    public static double FromCelsius(double celsius) => Fahrenheit ? (celsius * 9 / 5) + 32 : celsius;

    /// <summary>Número sem unidade (ex.: "72"); sem valor ⇒ "—".</summary>
    public static string Number(double? celsius) => celsius is { } c ? FromCelsius(c).ToString("0", Pt) : "—";

    /// <summary>Número com unidade (ex.: "72 °C"); sem valor ⇒ "—".</summary>
    public static string Text(double? celsius) => celsius is { } c ? $"{Number(c)} {Unit}" : "—";
}
