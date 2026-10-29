using System.IO.MemoryMappedFiles;

namespace KillerSense.Core;

/// <summary>
/// Batimento entre o serviço e o watchdog: memória compartilhada nomeada com o instante do último ciclo
/// e se há fan em manual. O watchdog devolve o Auto se o batimento parar com manual ativo (§6.3).
/// </summary>
public sealed class Heartbeat : IDisposable
{
    /// <summary>Nome global (sessão 0). Processos de console do mesmo usuário admin também enxergam.</summary>
    public const string DefaultName = @"Global\KillerSense.Heartbeat";

    private const int Size = 16; // [0..7] ticks UTC, [8..11] manual (0/1), [12..15] reservado
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;

    private Heartbeat(MemoryMappedFile map)
    {
        _map = map;
        _view = map.CreateViewAccessor(0, Size);
    }

    /// <summary>Cria (serviço) ou abre a região compartilhada.</summary>
    public static Heartbeat CreateOrOpen(string name = DefaultName) =>
        new(MemoryMappedFile.CreateOrOpen(name, Size, MemoryMappedFileAccess.ReadWrite));

    /// <summary>Abre uma região já criada pelo serviço; null se ela ainda não existe.</summary>
    public static Heartbeat? TryOpen(string name = DefaultName)
    {
        try
        {
            return new Heartbeat(MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public void Beat(DateTime utcNow, bool manualActive)
    {
        _view.Write(8, manualActive ? 1 : 0);
        _view.Write(0, utcNow.Ticks);
    }

    /// <summary>Marca que não há mais manual ativo (usado pelo watchdog depois de restaurar o Auto).</summary>
    public void ClearManual() => _view.Write(8, 0);

    public (DateTime LastBeatUtc, bool ManualActive) Read() =>
        (new DateTime(_view.ReadInt64(0), DateTimeKind.Utc), _view.ReadInt32(8) != 0);

    public void Dispose()
    {
        _view.Dispose();
        _map.Dispose();
    }
}
