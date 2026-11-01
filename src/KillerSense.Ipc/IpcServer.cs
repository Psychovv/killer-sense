using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace KillerSense.Ipc;

/// <summary>
/// Servidor do pipe no serviço. Aceita várias conexões (bandeja + janela), responde comandos pelo
/// <see cref="Handler"/> e envia o estado aos assinantes a cada <see cref="Publish"/>.
/// </summary>
public sealed class IpcServer
{
    private const int MaxClients = 8;
    private readonly string _pipeName;
    private readonly Func<IpcRequestType, Task<IpcMessage>> _handler;
    private readonly ConcurrentDictionary<LineChannel, byte> _subscribers = new();
    private volatile string? _lastStateLine;

    /// <param name="pipeName">Nome do pipe (sem <c>\\.\pipe\</c>).</param>
    /// <param name="handler">Executa RestoreAuto/Resume e devolve o ack (GetState/Subscribe são tratados aqui).</param>
    public IpcServer(string pipeName, Func<IpcRequestType, Task<IpcMessage>> handler)
    {
        _pipeName = pipeName;
        _handler = handler;
    }

    /// <summary>
    /// ACL do pipe: o próprio processo do servidor (SYSTEM no serviço instalado), SYSTEM e Administradores
    /// com controle total; usuários conectados na máquina (INTERACTIVE) podem ler e escrever. Ninguém pela rede.
    /// </summary>
    public static PipeSecurity CreateSecurity()
    {
        var s = new PipeSecurity();
        using (var me = WindowsIdentity.GetCurrent())
        {
            // necessário para abrir novas instâncias do pipe (FILE_CREATE_PIPE_INSTANCE)
            s.AddAccessRule(new PipeAccessRule(me.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return s;
    }

    /// <summary>Envia o estado a todos os assinantes (sem esperar; conexões quebradas são removidas).</summary>
    public void Publish(ServiceState state)
    {
        var line = IpcProtocol.Serialize(IpcMessage.ForState(state));
        _lastStateLine = line;
        foreach (var ch in _subscribers.Keys)
        {
            _ = SendOrDropAsync(ch, line);
        }
    }

    /// <summary>Laço de aceitação; termina com o cancelamento.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var security = CreateSecurity();
        while (!ct.IsCancellationRequested)
        {
            var pipe = NamedPipeServerStreamAcl.Create(
                _pipeName, PipeDirection.InOut, MaxClients, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 4096, 4096, security);
            try
            {
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                throw;
            }

            _ = ServeAsync(new LineChannel(pipe), ct);
        }
    }

    private async Task ServeAsync(LineChannel ch, CancellationToken ct)
    {
        try
        {
            while (await ch.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                var request = IpcProtocol.ParseRequest(line);
                if (request is null)
                {
                    await ch.WriteLineAsync(IpcProtocol.Serialize(IpcMessage.Ack(false, "Pedido inválido.")), ct).ConfigureAwait(false);
                    continue;
                }

                switch (request.Type)
                {
                    case IpcRequestType.GetState:
                    case IpcRequestType.Subscribe:
                        if (request.Type == IpcRequestType.Subscribe)
                        {
                            _subscribers.TryAdd(ch, 0);
                        }

                        if (_lastStateLine is { } last)
                        {
                            await ch.WriteLineAsync(last, ct).ConfigureAwait(false);
                        }

                        break;
                    default:
                        var ack = await _handler(request.Type).ConfigureAwait(false);
                        await ch.WriteLineAsync(IpcProtocol.Serialize(ack), ct).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            // cliente saiu, mandou lixo ou o serviço está parando
        }
        finally
        {
            _subscribers.TryRemove(ch, out _);
            await ch.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SendOrDropAsync(LineChannel ch, string line)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await ch.WriteLineAsync(line, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            _subscribers.TryRemove(ch, out _);
        }
    }
}
