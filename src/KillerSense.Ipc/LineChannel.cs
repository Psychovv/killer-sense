using System.Text;

namespace KillerSense.Ipc;

/// <summary>
/// Linhas UTF-8 sobre um stream: leitura com limite de tamanho (linha grande demais ⇒ <see cref="InvalidDataException"/>)
/// e escrita serializada (várias tarefas podem escrever sem misturar mensagens).
/// </summary>
public sealed class LineChannel : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly byte[] _buffer = new byte[4096];
    private int _start;
    private int _end;

    public LineChannel(Stream stream) => _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>Próxima linha (sem o \n), ou null quando a outra ponta fechou.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        using var line = new MemoryStream();
        while (true)
        {
            for (var i = _start; i < _end; i++)
            {
                if (_buffer[i] == (byte)'\n')
                {
                    line.Write(_buffer, _start, i - _start);
                    _start = i + 1;
                    return Decode(line);
                }
            }

            line.Write(_buffer, _start, _end - _start);
            _start = _end = 0;
            if (line.Length > IpcProtocol.MaxLineLength)
            {
                throw new InvalidDataException("Linha maior que o limite do protocolo.");
            }

            var n = await _stream.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
            if (n == 0)
            {
                return null;
            }

            _end = n;
        }
    }

    public async Task WriteLineAsync(string line, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(line);
        var bytes = Utf8.GetBytes(line + "\n");
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }

    private static string Decode(MemoryStream line)
    {
        if (line.Length > IpcProtocol.MaxLineLength)
        {
            throw new InvalidDataException("Linha maior que o limite do protocolo.");
        }

        return Utf8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
    }
}
