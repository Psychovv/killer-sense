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
}

public sealed record IpcRequest([property: JsonRequired] IpcRequestType Type);

/// <summary>Mensagem do serviço: <c>state</c> (estado) ou <c>ack</c> (resposta a um comando).</summary>
public sealed record IpcMessage(string Kind, ServiceState? State = null, bool? Ok = null, string? Error = null)
{
    public const string KindState = "state";
    public const string KindAck = "ack";

    public static IpcMessage ForState(ServiceState state) => new(KindState, State: state);

    public static IpcMessage Ack(bool ok, string? error = null) => new(KindAck, Ok: ok, Error: error);
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
    string? Message)
{
    public static ServiceState From(EngineSnapshot s, DateTime utcNow)
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
            s.Message);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcMessage))]
internal sealed partial class IpcJsonContext : JsonSerializerContext;
