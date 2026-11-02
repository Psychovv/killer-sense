namespace KillerSense.Service;

/// <summary>
/// Lembra se o usuário encerrou o killer-sense ("Encerrar" na bandeja): enquanto o marcador existir,
/// o serviço sobe desativado (Auto do EC, PredatorSense no controle), inclusive depois de reiniciar.
/// </summary>
public sealed class ControlStateStore
{
    private readonly string _markerPath;

    public ControlStateStore(string markerPath) => _markerPath = markerPath;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KillerSense", "control-disabled");

    public bool IsDisabled => File.Exists(_markerPath);

    public void SetDisabled(bool disabled)
    {
        if (disabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            File.WriteAllText(_markerPath, $"Desativado pelo usuário em {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
        }
        else if (File.Exists(_markerPath))
        {
            File.Delete(_markerPath);
        }
    }
}
