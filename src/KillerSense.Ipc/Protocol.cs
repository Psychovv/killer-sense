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
}

public sealed record IpcRequest([property: JsonRequired] IpcRequestType Type, FanProfileDto? Profile = null, OperatingMode? Mode = null);

/// <summary>
/// Mensagem do serviço: <c>state</c> (estado), <c>ack</c> (resposta a um comando) ou <c>profile</c>
/// (resposta a <see cref="IpcRequestType.GetProfile"/>).
/// </summary>
public sealed record IpcMessage(string Kind, ServiceState? State = null, bool? Ok = null, string? Error = null, FanProfileDto? Profile = null)
{
    public const string KindState = "state";
    public const string KindAck = "ack";
    public const string KindProfile = "profile";

    public static IpcMessage ForState(ServiceState state) => new(KindState, State: state);

    public static IpcMessage Ack(bool ok, string? error = null) => new(KindAck, Ok: ok, Error: error);

    public static IpcMessage ForProfile(FanProfileDto profile) => new(KindProfile, Ok: true, Profile: profile);
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
    bool? OnAc = null)
{
    /// <param name="onAc">Fonte de energia (true = tomada); null = desconhecida.</param>
    public static ServiceState From(EngineSnapshot s, DateTime utcNow, bool? onAc = null)
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
            onAc);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcMessage))]
[JsonSerializable(typeof(FanProfileDto))]
internal sealed partial class IpcJsonContext : JsonSerializerContext
{
    private static IpcJsonContext? s_indented;

    /// <summary>Mesmo contexto com indentação (para o arquivo de configuração). Criado sob demanda, depois de <c>Default</c>.</summary>
    public static IpcJsonContext Indented =>
        s_indented ??= new(new System.Text.Json.JsonSerializerOptions(Default.Options!) { WriteIndented = true });
}
