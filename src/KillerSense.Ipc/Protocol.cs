using System.Text.Json;
using System.Text.Json.Serialization;
using KillerSense.Core;

namespace KillerSense.Ipc;

/// <summary>
/// Protocolo entre o serviço e a interface (docs/arquitetura.md §9): uma mensagem JSON por linha (UTF-8)
/// num pipe nomeado. O serviço valida tudo; pedidos desconhecidos ou grandes demais são recusados.
/// </summary>
public static class IpcProtocol
{
    public const int Version = 1;

    /// <summary>Nome do pipe do serviço instalado.</summary>
    public const string PipeName = "killer-sense";

    /// <summary>Nome do pipe no modo --simulated (não conflita com o serviço instalado).</summary>
    public const string SimulatedPipeName = "killer-sense-simulated";

    /// <summary>Tamanho máximo de uma linha (bytes/caracteres). Pedidos maiores encerram a conexão.</summary>
    public const int MaxLineLength = 4096;

    public static string Serialize(IpcRequest request) =>
        JsonSerializer.Serialize(request, IpcJsonContext.Default.IpcRequest);

    public static string Serialize(IpcMessage message) =>
        JsonSerializer.Serialize(message, IpcJsonContext.Default.IpcMessage);

    /// <summary>Lê um pedido; null se o JSON for inválido.</summary>
    public static IpcRequest? ParseRequest(string line)
    {
        try
        {
            var r = JsonSerializer.Deserialize(line, IpcJsonContext.Default.IpcRequest);
            return r is not null && Enum.IsDefined(r.Type) ? r : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Perfil em JSON legível (config.json).</summary>
    public static string SerializeProfile(FanProfileDto profile) =>
        JsonSerializer.Serialize(profile, IpcJsonContext.Indented.FanProfileDto);

    /// <summary>Lê um perfil de config.json; null se o JSON for inválido (a validação vem depois).</summary>
    public static FanProfileDto? ParseProfile(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, IpcJsonContext.Default.FanProfileDto);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Configuração do offset da GPU em JSON legível (gpu-offsets.json).</summary>
    public static string SerializeGpuOffsets(GpuOffsetSettingsDto settings) =>
        JsonSerializer.Serialize(settings, IpcJsonContext.Indented.GpuOffsetSettingsDto);

    /// <summary>Lê a configuração do offset da GPU; null se o JSON for inválido (a validação vem depois).</summary>
    public static GpuOffsetSettingsDto? ParseGpuOffsets(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, IpcJsonContext.Default.GpuOffsetSettingsDto);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Configuração da luz em JSON legível (lighting.json).</summary>
    public static string SerializeLighting(LightingSettingsDto settings) =>
        JsonSerializer.Serialize(settings, IpcJsonContext.Indented.LightingSettingsDto);

    /// <summary>Lê a configuração da luz; null se o JSON for inválido (a validação vem depois).</summary>
    public static LightingSettingsDto? ParseLighting(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, IpcJsonContext.Default.LightingSettingsDto);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lê uma mensagem do serviço; null se o JSON for inválido.</summary>
    public static IpcMessage? ParseMessage(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, IpcJsonContext.Default.IpcMessage);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Pedidos que a interface pode fazer.</summary>
public enum IpcRequestType
{
    /// <summary>Devolve o estado atual uma vez.</summary>
    GetState,

    /// <summary>Passa a receber o estado a cada ciclo (1 s).</summary>
    Subscribe,

    /// <summary>"Voltar ao Auto": fans no Auto do EC e controle pausado até <see cref="Resume"/>.</summary>
    RestoreAuto,

    /// <summary>Retoma o controle (recomeça do Auto).</summary>
    Resume,

    /// <summary>Devolve o perfil de fans em uso.</summary>
    GetProfile,

    /// <summary>Valida, aplica e salva um perfil de fans (campo <c>profile</c> obrigatório).</summary>
    SetProfile,

    /// <summary>"Encerrar killer-sense": Auto do EC e controle desativado (persiste até <see cref="Enable"/>).</summary>
    Disable,

    /// <summary>Reativa o controle (recomeça do Auto) e grava isso.</summary>
    Enable,

    /// <summary>Troca o modo de operação (campo <c>mode</c> obrigatório). Não fica "preso": o PredatorSense pode trocar depois.</summary>
    SetOperatingMode,

    /// <summary>Liga/desliga o limite de carga de 80% (campo <c>enabled</c> obrigatório). Não fica "preso".</summary>
    SetChargeLimit,

    /// <summary>Devolve a configuração do offset da GPU por modo.</summary>
    GetGpuOffsets,

    /// <summary>Valida, salva e aplica a configuração do offset da GPU (campo <c>gpuOffsets</c> obrigatório).</summary>
    SetGpuOffsets,

    /// <summary>Devolve a configuração da luz e, com o controle da luz ligado, o que o teclado tem agora.</summary>
    GetLighting,

    /// <summary>Valida e salva a configuração da luz (campo <c>lighting</c>); com o controle ligado, grava no teclado e confere.</summary>
    SetLighting,
}

public sealed record IpcRequest([property: JsonRequired] IpcRequestType Type, FanProfileDto? Profile = null, OperatingMode? Mode = null, bool? Enabled = null, GpuOffsetSettingsDto? GpuOffsets = null, LightingSettingsDto? Lighting = null);

/// <summary>
/// Mensagem do serviço: <c>state</c> (estado), <c>ack</c> (resposta a um comando) ou <c>profile</c>
/// (resposta a <see cref="IpcRequestType.GetProfile"/>).
/// </summary>
public sealed record IpcMessage(
    string Kind,
    ServiceState? State = null,
    bool? Ok = null,
    string? Error = null,
    FanProfileDto? Profile = null,
    GpuOffsetSettingsDto? GpuOffsets = null,
    LightingSettingsDto? Lighting = null,
    KeyboardLightingDto? LightingCurrent = null)
{
    public const string KindState = "state";
    public const string KindAck = "ack";
    public const string KindProfile = "profile";
    public const string KindGpuOffsets = "gpuOffsets";
    public const string KindLighting = "lighting";

    public static IpcMessage ForState(ServiceState state) => new(KindState, State: state);

    public static IpcMessage Ack(bool ok, string? error = null) => new(KindAck, Ok: ok, Error: error);

    public static IpcMessage ForProfile(FanProfileDto profile) => new(KindProfile, Ok: true, Profile: profile);

    public static IpcMessage ForGpuOffsets(GpuOffsetSettingsDto settings) => new(KindGpuOffsets, Ok: true, GpuOffsets: settings);

    /// <param name="current">O que o teclado tem agora; null = controle da luz desligado ou leitura falhou.</param>
    /// <param name="readError">Erro da leitura do teclado, se houver (a configuração vem mesmo assim).</param>
    public static IpcMessage ForLighting(LightingSettingsDto settings, KeyboardLightingDto? current, string? readError = null) =>
        new(KindLighting, Ok: true, Error: readError, Lighting: settings, LightingCurrent: current);
}

/// <summary>Estado publicado pelo serviço a cada ciclo.</summary>
public sealed record ServiceState(
    int ProtocolVersion,
    DateTime TimestampUtc,
    EngineState State,
    double? CpuTemperature,
    double? GpuTemperature,
    OperatingMode? OperatingMode,
    FanMode CpuFanMode,
    int? CpuFanPercent,
    int? CpuFanRpm,
    FanMode GpuFanMode,
    int? GpuFanPercent,
    int? GpuFanRpm,
    string? Message,
    bool? OnAc = null,
    bool? ChargeLimitOn = null,
    GpuTelemetry? Gpu = null,
    GpuOffsetStatus? GpuOffset = null)
{
    /// <param name="onAc">Fonte de energia (true = tomada); null = desconhecida.</param>
    /// <param name="chargeLimitOn">Limite de carga de 80%; null = desconhecido (desativado, sem leitura).</param>
    /// <param name="gpu">Clocks, uso e potência da GPU (só com ela ligada); null = desligada ou sem dados.</param>
    /// <param name="gpuOffset">Estado do offset de clock da GPU; null = sem controlador (versão antiga).</param>
    public static ServiceState From(EngineSnapshot s, DateTime utcNow, bool? onAc = null, bool? chargeLimitOn = null, GpuTelemetry? gpu = null, GpuOffsetStatus? gpuOffset = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(
            IpcProtocol.Version,
            utcNow,
            s.State,
            s.CpuTemperature,
            s.GpuTemperature,
            s.OperatingMode,
            s.Cpu.Mode,
            s.Cpu.Percent,
            s.CpuFanRpm,
            s.Gpu.Mode,
            s.Gpu.Percent,
            s.GpuFanRpm,
            s.Message,
            onAc,
            chargeLimitOn,
            gpu,
            gpuOffset);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcMessage))]
[JsonSerializable(typeof(FanProfileDto))]
[JsonSerializable(typeof(GpuOffsetSettingsDto))]
[JsonSerializable(typeof(LightingSettingsDto))]
internal sealed partial class IpcJsonContext : JsonSerializerContext
{
    private static IpcJsonContext? s_indented;

    /// <summary>Mesmo contexto com indentação (para o arquivo de configuração). Criado sob demanda, depois de <c>Default</c>.</summary>
    public static IpcJsonContext Indented =>
        s_indented ??= new(new System.Text.Json.JsonSerializerOptions(Default.Options!) { WriteIndented = true });
}
