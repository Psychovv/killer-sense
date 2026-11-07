using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KillerSense.App.Settings;

/// <summary>
/// Preferências só da interface (unidade, cor de destaque, iniciar na bandeja), em
/// <c>%APPDATA%\KillerSense\app.json</c>. Nada aqui chega ao serviço nem ao firmware.
/// </summary>
public sealed partial class AppSettings : ObservableObject
{
    public const string DefaultAccent = "#8B7BFF";

    public static AppSettings Current { get; } = Load(DefaultPath);

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KillerSense", "app.json");

    [ObservableProperty] public partial bool Fahrenheit { get; set; }

    [ObservableProperty] public partial string Accent { get; set; } = DefaultAccent;

    /// <summary>Tema: <see cref="AppTheme.Grafite"/> (padrão) ou <see cref="AppTheme.Metal"/>.</summary>
    [ObservableProperty] public partial string Theme { get; set; } = AppTheme.Grafite;

    /// <summary>Ao iniciar com o Windows, fica só na bandeja (sem abrir a janela).</summary>
    [ObservableProperty] public partial bool StartInTray { get; set; } = true;

    /// <summary>Lê o arquivo; ausente ou inválido ⇒ padrões.</summary>
    public static AppSettings Load(string path)
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length < 16 * 1024
                && JsonSerializer.Deserialize(File.ReadAllText(path), AppSettingsJson.Default.AppSettingsData) is { } data)
            {
                settings.Fahrenheit = data.Fahrenheit;
                settings.StartInTray = data.StartInTray;
                settings.Theme = AppTheme.Normalize(data.Theme);
                if (data.Accent is { } accent && Color.TryParse(accent, out _))
                {
                    settings.Accent = accent;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // fica com os padrões
        }

        return settings;
    }

    /// <summary>Grava de forma atômica (arquivo temporário + troca).</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new AppSettingsData(Fahrenheit, Accent, StartInTray, Theme), AppSettingsJson.Default.AppSettingsData));
        File.Move(tmp, path, overwrite: true);
    }
}

internal sealed record AppSettingsData(bool Fahrenheit, string? Accent, bool StartInTray = true, string? Theme = null);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettingsData))]
internal sealed partial class AppSettingsJson : JsonSerializerContext;
