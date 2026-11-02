using System.IO.Pipes;
using System.Text;
using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.Tests;

public class IpcProtocolTests
{
    private static ServiceState SampleState() => new(
        IpcProtocol.Version, new DateTime(2026, 11, 6, 1, 0, 0, DateTimeKind.Utc), EngineState.Curve,
        72, 53, OperatingMode.Balanced, FanMode.Custom, 38, 4100, FanMode.Auto, null, 2290, null);

    [Fact]
    public void Estado_vai_e_volta_pelo_json_com_enums_em_texto()
    {
        var line = IpcProtocol.Serialize(IpcMessage.ForState(SampleState()));
        Assert.Contains("\"state\":\"Curve\"", line, StringComparison.Ordinal);
        Assert.Contains("\"cpuFanMode\":\"Custom\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', line);
        Assert.Equal(SampleState(), IpcProtocol.ParseMessage(line)!.State);
    }

    [Theory]
    [InlineData("{\"type\":\"RestoreAuto\"}", IpcRequestType.RestoreAuto)]
    [InlineData("{\"type\":\"Subscribe\"}", IpcRequestType.Subscribe)]
    public void Pedido_valido_e_aceito(string json, IpcRequestType expected) =>
        Assert.Equal(expected, IpcProtocol.ParseRequest(json)!.Type);

    [Theory]
    [InlineData("lixo")]
    [InlineData("{\"type\":\"FormatDisk\"}")]
    [InlineData("{\"type\":99}")]
    [InlineData("{}")]
    public void Pedido_invalido_e_recusado(string json) => Assert.Null(IpcProtocol.ParseRequest(json));

    [Fact]
    public async Task Linha_maior_que_o_limite_e_recusada()
    {
        var big = new string('a', IpcProtocol.MaxLineLength + 10) + "\n";
        await using var ch = new LineChannel(new MemoryStream(Encoding.UTF8.GetBytes(big)));
        await Assert.ThrowsAsync<InvalidDataException>(() => ch.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Le_varias_linhas_do_mesmo_bloco()
    {
        await using var ch = new LineChannel(new MemoryStream(Encoding.UTF8.GetBytes("um\r\ndois\n")));
        Assert.Equal("um", await ch.ReadLineAsync(CancellationToken.None));
        Assert.Equal("dois", await ch.ReadLineAsync(CancellationToken.None));
        Assert.Null(await ch.ReadLineAsync(CancellationToken.None));
    }
}

public class IpcPipeTests
{
    private static ServiceState State(EngineState s) => new(
        IpcProtocol.Version, DateTime.UtcNow, s, 70, null, OperatingMode.Balanced,
        FanMode.Auto, null, 2100, FanMode.Auto, null, 1800, null);

    [Fact]
    public async Task Cliente_recebe_estado_e_comando_recebe_ack()
    {
        var name = $"killer-sense-test-{Guid.NewGuid():N}";
        var commands = new List<IpcRequestType>();
        var server = new IpcServer(name, r =>
        {
            commands.Add(r.Type);
            return Task.FromResult(r.Type == IpcRequestType.GetProfile
                ? IpcMessage.ForProfile(FanProfileDto.From(FanProfile.Default))
                : IpcMessage.Ack(true));
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serverTask = server.RunAsync(cts.Token);

        await using var client = new ServiceClient(name);
        var received = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StateReceived += s => received.TrySetResult(s);
        client.Start();

        // publica até o cliente assinar e receber
        while (!received.Task.IsCompleted && !cts.IsCancellationRequested)
        {
            server.Publish(State(EngineState.Curve));
            await Task.Delay(100, cts.Token);
        }

        Assert.Equal(EngineState.Curve, (await received.Task).State);
        var ack = await client.SendCommandAsync(IpcRequestType.RestoreAuto, TimeSpan.FromSeconds(5));
        Assert.True(ack.Ok);
        var profile = await client.RequestAsync(new IpcRequest(IpcRequestType.GetProfile), TimeSpan.FromSeconds(5));
        Assert.Equal(IpcMessage.KindProfile, profile.Kind);
        Assert.Equal(60, profile.Profile!.CpuEntryTemperature);
        Assert.Equal([IpcRequestType.RestoreAuto, IpcRequestType.GetProfile], commands);

        await cts.CancelAsync();
        await serverTask;
    }

    [Fact]
    public async Task Servidor_responde_erro_para_pedido_invalido()
    {
        var name = $"killer-sense-test-{Guid.NewGuid():N}";
        var server = new IpcServer(name, _ => Task.FromResult(IpcMessage.Ack(true)));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serverTask = server.RunAsync(cts.Token);

        await using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await pipe.ConnectAsync(5000, cts.Token);
            await using var ch = new LineChannel(pipe);
            await ch.WriteLineAsync("{\"type\":\"FormatDisk\"}", cts.Token);
            var reply = IpcProtocol.ParseMessage((await ch.ReadLineAsync(cts.Token))!);
            Assert.Equal(IpcMessage.KindAck, reply!.Kind);
            Assert.False(reply.Ok);
        }

        await cts.CancelAsync();
        await serverTask;
    }
}
