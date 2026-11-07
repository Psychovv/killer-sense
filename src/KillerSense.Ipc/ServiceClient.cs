using System.IO.Pipes;

namespace KillerSense.Ipc;

/// <summary>
/// Cliente do serviço para a interface (janela e bandeja). Conecta, assina o estado e reconecta sozinho
/// se o serviço reiniciar. Os eventos chegam numa thread de fundo (a interface faz o dispatch para a UI).
/// </summary>
public sealed class ServiceClient : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _ackGate = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private TaskCompletionSource<IpcMessage>? _pendingAck;
    private LineChannel? _channel;
    private Task? _loop;

    public ServiceClient(string pipeName = IpcProtocol.PipeName) => _pipeName = pipeName;

    /// <summary>Novo estado recebido do serviço.</summary>
    public event Action<ServiceState>? StateReceived;

    /// <summary>Conexão com o serviço mudou (true = conectado).</summary>
    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _channel is not null;

    /// <summary>Começa a conectar e assinar em segundo plano.</summary>
    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    /// <summary>Envia um comando (RestoreAuto/Resume) e espera o ack do serviço.</summary>
    public Task<IpcMessage> SendCommandAsync(IpcRequestType type, TimeSpan timeout) =>
        RequestAsync(new IpcRequest(type), timeout);

    /// <summary>Envia um pedido e espera a resposta (ack, profile, gpuOffsets, lighting…). Um pedido por vez.</summary>
    public async Task<IpcMessage> RequestAsync(IpcRequest request, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _requestGate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            var ch = _channel ?? throw new InvalidOperationException("Sem conexão com o serviço killer-sense.");
            var tcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_ackGate)
            {
                _pendingAck = tcs;
            }

            await ch.WriteLineAsync(IpcProtocol.Serialize(request), _cts.Token).ConfigureAwait(false);
            return await tcs.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        finally
        {
            lock (_ackGate)
            {
                _pendingAck = null;
            }

            _requestGate.Release();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(2000, ct).ConfigureAwait(false);
                var ch = new LineChannel(pipe);
                _channel = ch;
                ConnectionChanged?.Invoke(true);
                await ch.WriteLineAsync(IpcProtocol.Serialize(new IpcRequest(IpcRequestType.Subscribe)), ct).ConfigureAwait(false);
                while (await ch.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    var msg = IpcProtocol.ParseMessage(line);
                    if (msg?.Kind == IpcMessage.KindState && msg.State is { } state)
                    {
                        StateReceived?.Invoke(state);
                    }
                    else if (msg is not null && msg.Kind != IpcMessage.KindState) // ack ou resposta a um pedido
                    {
                        TaskCompletionSource<IpcMessage>? tcs;
                        lock (_ackGate)
                        {
                            tcs = _pendingAck;
                            _pendingAck = null;
                        }

                        tcs?.TrySetResult(msg);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or UnauthorizedAccessException)
            {
                // serviço fora do ar ou reiniciando: tenta de novo
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                if (_channel is not null)
                {
                    _channel = null;
                    ConnectionChanged?.Invoke(false);
                }

                await pipe.DisposeAsync().ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // encerrando
            }
        }

        _cts.Dispose();
        _requestGate.Dispose();
    }
}
