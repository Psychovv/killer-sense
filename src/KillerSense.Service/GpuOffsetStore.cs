using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.Service;

/// <summary>
/// Configuração do offset da GPU em <c>%ProgramData%\KillerSense\gpu-offsets.json</c>. Só o serviço grava
/// (atômico, com backup). Arquivo ausente ou inválido ⇒ padrão (DESLIGADO, tabela da Acer).
/// </summary>
public sealed class GpuOffsetStore
{
    private readonly string _path;

    public GpuOffsetStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KillerSense", "gpu-offsets.json");

    public GpuOffsetSettings Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(_path))
        {
            return GpuOffsetSettings.Default;
        }

        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"não foi possível ler {_path}: {ex.Message}";
            return GpuOffsetSettings.Default;
        }

        if (json.Length > 16 * 1024 || IpcProtocol.ParseGpuOffsets(json) is not { } dto)
        {
            problem = $"{_path} não é uma configuração válida (JSON).";
            return GpuOffsetSettings.Default;
        }

        if (dto.TryToSettings(out var error) is not { } settings)
        {
            problem = $"{_path} rejeitado: {error}";
            return GpuOffsetSettings.Default;
        }

        return settings;
    }

    public void Save(GpuOffsetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, IpcProtocol.SerializeGpuOffsets(GpuOffsetSettingsDto.From(settings)));
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
