using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.Service;

/// <summary>
/// Perfil de fans em <c>%ProgramData%\KillerSense\config.json</c> (docs/arquitetura.md §10).
/// Só o serviço grava, de forma atômica e com backup; ao ler, valida tudo — arquivo ausente ou inválido
/// ⇒ perfil padrão.
/// </summary>
public sealed class ProfileStore
{
    private readonly string _path;

    public ProfileStore(string path) => _path = path;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KillerSense", "config.json");

    /// <summary>Carrega o perfil; em caso de problema devolve o padrão e o motivo em <paramref name="problem"/>.</summary>
    public FanProfile Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(_path))
        {
            return FanProfile.Default;
        }

        string json;
        try
        {
            json = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"não foi possível ler {_path}: {ex.Message}";
            return FanProfile.Default;
        }

        if (json.Length > 64 * 1024 || IpcProtocol.ParseProfile(json) is not { } dto)
        {
            problem = $"{_path} não é um perfil válido (JSON).";
            return FanProfile.Default;
        }

        if (dto.TryToProfile(out var error) is not { } profile)
        {
            problem = $"{_path} rejeitado: {error}";
            return FanProfile.Default;
        }

        return profile;
    }

    /// <summary>Grava atomicamente (arquivo temporário + troca), mantendo o anterior em <c>config.json.bak</c>.</summary>
    public void Save(FanProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, IpcProtocol.SerializeProfile(FanProfileDto.From(profile)));
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
