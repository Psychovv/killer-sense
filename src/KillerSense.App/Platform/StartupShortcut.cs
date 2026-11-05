using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace KillerSense.App.Platform;

/// <summary>
/// "Iniciar com o Windows": um atalho na pasta Inicializar do usuário
/// (<c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup</c>). Sem registro e sem admin; apagar o
/// atalho desfaz. Criado pela API de atalhos do Windows (IShellLinkW), sem COM em tempo de execução.
/// </summary>
internal static partial class StartupShortcut
{
    /// <summary>Argumento que o atalho passa: o app sabe que foi aberto pelo Windows.</summary>
    public const string AutostartArg = "--autostart";

    public static string LinkPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "killer-sense.lnk");

    public static bool Exists => File.Exists(LinkPath);

    /// <summary>Cria ou substitui o atalho para o executável atual.</summary>
    public static void Create(bool trayOnly)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Caminho do programa desconhecido.");
        var clsid = ShellLinkClsid;
        var iid = typeof(IShellLinkW).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, ClsctxInprocServer, in iid, out var ptr));
        try
        {
            var link = (IShellLinkW)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
            link.SetPath(exe);
            link.SetArguments(trayOnly ? $"{AutostartArg} --tray" : AutostartArg);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            link.SetIconLocation(exe, 0);
            link.SetDescription("killer-sense - iniciar com o Windows");
            ((IPersistFile)link).Save(LinkPath, true);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    public static void Delete()
    {
        if (Exists)
        {
            File.Delete(LinkPath);
        }
    }

    private const uint ClsctxInprocServer = 1;
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);
}

// Ordem da vtable igual à do ShObjIdl_core.h; os métodos não usados ficam com assinaturas simples.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(nint pszFile, int cch, nint pfd, uint fFlags);
    void GetIDList(out nint ppidl);
    void SetIDList(nint pidl);
    void GetDescription(nint pszName, int cch);
    void SetDescription(string pszName);
    void GetWorkingDirectory(nint pszDir, int cch);
    void SetWorkingDirectory(string pszDir);
    void GetArguments(nint pszArgs, int cch);
    void SetArguments(string pszArgs);
    void GetHotkey(out ushort pwHotkey);
    void SetHotkey(ushort wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation(nint pszIconPath, int cch, out int piIcon);
    void SetIconLocation(string pszIconPath, int iIcon);
    void SetRelativePath(string pszPathRel, uint dwReserved);
    void Resolve(nint hwnd, uint fFlags);
    void SetPath(string pszFile);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load(string pszFileName, uint dwMode);
    void Save(string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted(string pszFileName);
    void GetCurFile(out nint ppszFileName);
}
