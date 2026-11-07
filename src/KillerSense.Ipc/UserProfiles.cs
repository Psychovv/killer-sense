using KillerSense.Core;

namespace KillerSense.Ipc;

/// <summary>Fans de um perfil: no Auto do EC (controle pausado) ou numa curva guardada no perfil.</summary>
public sealed record ProfileFans(bool Auto, FanProfileDto? Curve = null);

/// <summary>
/// Perfil do usuário: junta modo, fans, offset da GPU, limite de carga e luz do teclado. Cada parte nula
/// fica como está ao aplicar. Guardado só pela interface (<c>%APPDATA%\KillerSense\profiles.json</c>); o
/// serviço valida cada parte de novo quando o perfil é aplicado.
/// </summary>
public sealed record UserProfile(
    string Name,
    OperatingMode? Mode = null,
    ProfileFans? Fans = null,
    GpuOffsetSettingsDto? GpuOffsets = null,
    bool? ChargeLimit = null,
    KeyboardLightingDto? Lighting = null)
{
    public const int MaxNameLength = 40;

    /// <summary>
    /// "Padrão Acer", somente leitura: Equilibrado, fans no Auto do EC e o offset da GPU da Acer (só o Turbo
    /// acelera). Bateria e luz ficam como estão.
    /// </summary>
    public static UserProfile AcerDefault { get; } = new(
        "Padrão Acer",
        OperatingMode.Balanced,
        new ProfileFans(Auto: true),
        GpuOffsetSettingsDto.From(GpuOffsetSettings.Default with { Enabled = true }));

    /// <summary>Null se o perfil for válido; senão, o motivo.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength)
        {
            return $"Nome vazio ou com mais de {MaxNameLength} letras.";
        }

        if (Mode is { } mode && !Enum.IsDefined(mode))
        {
            return $"“{Name}”: modo desconhecido ({(int)mode}).";
        }

        if (Fans is { Auto: false } fans && (fans.Curve is null || fans.Curve.TryToProfile(out _) is null))
        {
            return $"“{Name}”: curva de fans ausente ou inválida.";
        }

        if (GpuOffsets is not null && GpuOffsets.TryToSettings(out var gpuError) is null)
        {
            return $"“{Name}”: offset da GPU inválido ({gpuError}).";
        }

        if (Lighting is not null && Lighting.TryToLighting(out var lightError) is null)
        {
            return $"“{Name}”: luz inválida ({lightError}).";
        }

        return null;
    }
}

/// <summary>Arquivo de perfis: a lista e o último aplicado.</summary>
public sealed record UserProfileSet(IReadOnlyList<UserProfile> Profiles, string? LastApplied = null)
{
    public const int MaxProfiles = 50;

    /// <summary>Tamanho máximo do arquivo (bytes), para não ler qualquer coisa.</summary>
    public const int MaxFileLength = 256 * 1024;

    public static UserProfileSet Empty { get; } = new([]);

    /// <summary>Null se tudo for válido (inclusive nomes únicos e diferentes de "Padrão Acer"); senão, o motivo.</summary>
    public string? Validate()
    {
        if (Profiles is null || Profiles.Count > MaxProfiles)
        {
            return $"Lista de perfis ausente ou com mais de {MaxProfiles}.";
        }

        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase) { UserProfile.AcerDefault.Name };
        foreach (var p in Profiles)
        {
            if (p is null)
            {
                return "Perfil vazio na lista.";
            }

            if (p.Validate() is { } error)
            {
                return error;
            }

            if (!names.Add(p.Name.Trim()))
            {
                return $"Nome repetido: “{p.Name}”.";
            }
        }

        return null;
    }
}

/// <summary>Arquivo de perfis (da interface ou exportado): leitura validada e gravação atômica.</summary>
public static class UserProfileFile
{
    public static string DefaultPath(bool simulated) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KillerSense", simulated ? "profiles.simulated.json" : "profiles.json");

    /// <summary>Lê e valida um JSON de perfis (arquivo ou importação). Null com o motivo se não servir.</summary>
    public static UserProfileSet? Parse(string json, out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > UserProfileSet.MaxFileLength || IpcProtocol.ParseUserProfiles(json) is not { } set)
        {
            error = "Não é um arquivo de perfis do killer-sense (JSON).";
            return null;
        }

        error = set.Validate();
        return error is null ? set with { Profiles = [.. set.Profiles.Select(p => p with { Name = p.Name.Trim() })] } : null;
    }

    /// <summary>Arquivo ausente ⇒ lista vazia; inválido ⇒ lista vazia com o motivo (o arquivo fica intacto).</summary>
    public static UserProfileSet Load(string path, out string? problem)
    {
        problem = null;
        try
        {
            if (!File.Exists(path))
            {
                return UserProfileSet.Empty;
            }

            if (new FileInfo(path).Length > UserProfileSet.MaxFileLength)
            {
                problem = $"{path} é grande demais.";
                return UserProfileSet.Empty;
            }

            if (Parse(File.ReadAllText(path), out var error) is { } set)
            {
                return set;
            }

            problem = $"{path} ignorado: {error}";
            return UserProfileSet.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"não foi possível ler {path}: {ex.Message}";
            return UserProfileSet.Empty;
        }
    }

    /// <summary>Grava (atômico, com backup). Lança se o conjunto for inválido.</summary>
    public static void Save(string path, UserProfileSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.Validate() is { } error)
        {
            throw new ArgumentException(error, nameof(set));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, IpcProtocol.SerializeUserProfiles(set));
        if (File.Exists(path))
        {
            File.Replace(tmp, path, path + ".bak");
        }
        else
        {
            File.Move(tmp, path);
        }
    }
}

/// <summary>Resultado de uma parte do perfil ao aplicar.</summary>
public sealed record ProfileStepResult(string Part, bool Ok, string Message);

/// <summary>
/// Aplica um perfil pelos comandos que o serviço já tem, parte por parte. Uma parte que falha não impede as
/// outras (são independentes); o resultado diz o que deu certo.
/// </summary>
public static class ProfileApplier
{
    /// <param name="send">Envia um pedido ao serviço e devolve a resposta (ex.: <see cref="ServiceClient.RequestAsync"/>).</param>
    /// <param name="onAc">Fonte de energia, se conhecida: modo indisponível nela vira Equilibrado.</param>
    public static async Task<IReadOnlyList<ProfileStepResult>> ApplyAsync(UserProfile profile, Func<IpcRequest, Task<IpcMessage>> send, bool? onAc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(send);
        if (profile.Validate() is { } invalid)
        {
            return [new ProfileStepResult("Perfil", false, invalid)];
        }

        var results = new List<ProfileStepResult>();

        if (profile.Mode is { } wanted)
        {
            var mode = wanted;
            var note = "";
            if (onAc is { } ac && !OperatingModes.IsAllowed(wanted, ac))
            {
                mode = OperatingMode.Balanced;
                note = ac ? " (Eco só existe na bateria)" : " (Desempenho e Turbo só na tomada)";
            }

            results.Add(await StepAsync("Modo", () => send(new IpcRequest(IpcRequestType.SetOperatingMode, Mode: mode)), $"{ModeName(mode)}{note}").ConfigureAwait(false));
        }

        if (profile.Fans is { } fans)
        {
            if (fans.Auto)
            {
                results.Add(await StepAsync("Fans", () => send(new IpcRequest(IpcRequestType.RestoreAuto)), "Auto do EC").ConfigureAwait(false));
            }
            else
            {
                var saved = await StepAsync("Fans", () => send(new IpcRequest(IpcRequestType.SetProfile, Profile: fans.Curve)), "curva do perfil").ConfigureAwait(false);
                if (saved.Ok)
                {
                    saved = await StepAsync("Fans", () => send(new IpcRequest(IpcRequestType.Resume)), "curva do perfil").ConfigureAwait(false);
                }

                results.Add(saved);
            }
        }

        if (profile.GpuOffsets is { } gpu)
        {
            results.Add(await StepAsync("GPU", () => send(new IpcRequest(IpcRequestType.SetGpuOffsets, GpuOffsets: gpu)), gpu.Enabled ? "offset por modo ligado" : "offset desligado").ConfigureAwait(false));
        }

        if (profile.ChargeLimit is { } limit)
        {
            results.Add(await StepAsync("Bateria", () => send(new IpcRequest(IpcRequestType.SetChargeLimit, Enabled: limit)), limit ? "limite de 80% ligado" : "carga completa").ConfigureAwait(false));
        }

        if (profile.Lighting is { } lighting)
        {
            results.Add(await ApplyLightingAsync(lighting, send).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>Uma frase para a interface: tudo certo ou o que falhou.</summary>
    public static string Summarize(string profileName, IReadOnlyList<ProfileStepResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            return $"Perfil “{profileName}” não muda nada.";
        }

        var failed = results.Where(r => !r.Ok).ToList();
        var done = string.Join(", ", results.Where(r => r.Ok).Select(r => $"{r.Part}: {r.Message}"));
        return failed.Count == 0
            ? $"Perfil “{profileName}” aplicado. {done}."
            : $"Perfil “{profileName}” aplicado em parte. Falhou — {string.Join("; ", failed.Select(r => $"{r.Part}: {r.Message}"))}."
              + (done.Length > 0 ? $" Aplicado — {done}." : "");
    }

    /// <summary>A luz respeita o liga/desliga do controle da luz: o perfil só troca a configuração.</summary>
    private static async Task<ProfileStepResult> ApplyLightingAsync(KeyboardLightingDto lighting, Func<IpcRequest, Task<IpcMessage>> send)
    {
        try
        {
            var current = await send(new IpcRequest(IpcRequestType.GetLighting)).ConfigureAwait(false);
            if (current.Lighting is not { } settings)
            {
                return new("Luz", false, current.Error ?? "o serviço não tem o controle da luz (versão antiga?)");
            }

            var ack = await send(new IpcRequest(IpcRequestType.SetLighting, Lighting: settings with { Lighting = lighting })).ConfigureAwait(false);
            return ack.Ok == true
                ? new("Luz", true, settings.Enabled ? "gravada no teclado" : "guardada (controle da luz desligado)")
                : new("Luz", false, ack.Error ?? "recusada pelo serviço");
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            return new("Luz", false, ex.Message);
        }
    }

    private static async Task<ProfileStepResult> StepAsync(string part, Func<Task<IpcMessage>> call, string okMessage)
    {
        try
        {
            var ack = await call().ConfigureAwait(false);
            return ack.Ok == true ? new(part, true, okMessage) : new(part, false, ack.Error ?? "recusado pelo serviço");
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            return new(part, false, ex.Message);
        }
    }

    public static string ModeName(OperatingMode mode) => mode switch
    {
        OperatingMode.Quiet => "Silencioso",
        OperatingMode.Balanced => "Equilibrado",
        OperatingMode.Performance => "Desempenho",
        OperatingMode.Turbo => "Turbo",
        OperatingMode.Eco => "Eco",
        _ => mode.ToString(),
    };
}
