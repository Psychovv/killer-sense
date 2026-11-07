using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.Service;

/// <summary>
/// Configuração da luz do teclado em <c>%ProgramData%\KillerSense\lighting.json</c>. Só o serviço grava
/// (atômico, com backup). Arquivo ausente ou inválido ⇒ padrão (controle DESLIGADO).
/// </summary>
public sealed class LightingStore
{
    private readonly string _path;

    public LightingStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KillerSense", "lighting.json");

    public LightingSettings Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(_path))
        {
            return LightingSettings.Default;
        }

        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"não foi possível ler {_path}: {ex.Message}";
            return LightingSettings.Default;
        }

        if (json.Length > 16 * 1024 || IpcProtocol.ParseLighting(json) is not { } dto)
        {
            problem = $"{_path} não é uma configuração válida (JSON).";
            return LightingSettings.Default;
        }

        if (dto.TryToSettings(out var error) is not { } settings)
        {
            problem = $"{_path} rejeitado: {error}";
            return LightingSettings.Default;
        }

        return settings;
    }

    public void Save(LightingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, IpcProtocol.SerializeLighting(LightingSettingsDto.From(settings)));
        if (File.Exists(_path))
        {
            File.Replace(tmp, _path, _path + ".bak");
        }
        else
        {
            File.Move(tmp, _path);
        }
    }
}
