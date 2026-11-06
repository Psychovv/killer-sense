namespace KillerSense.Core;

/// <summary>
/// Aplica o offset de clock da GPU do modo atual, como o serviço da Acer faz ao trocar de modo.
/// Regras:
/// <list type="bullet">
/// <item>Só fala com o driver quando há um evento (troca de modo, configuração, GPU religou, temperatura)
/// e a GPU está ligada (D0): nunca a acorda, e não fica regravando a cada ciclo.</item>
/// <item>Não briga: se o killer-sense nunca gravou nada e o alvo é zero, não toca no driver (um offset
/// posto por outro programa fica onde está).</item>
/// <item>Failsafe: GPU a <see cref="HotCelsius"/> °C ou mais, ou leitura inválida com ela ligada ⇒ volta a
/// zero o que o killer-sense gravou, até esfriar abaixo de <see cref="CoolCelsius"/> °C.</item>
/// <item>Desativado ("Encerrar") ⇒ zera o que gravou. <see cref="Release"/> zera na saída do serviço.</item>
/// <item>Toda escrita é limitada pela <see cref="GpuOffsetPolicy"/> e conferida lendo de volta.</item>
/// </list>
/// </summary>
public sealed class GpuOffsetController
{
    public const double HotCelsius = 85;
    public const double CoolCelsius = 78;
    public const double RetrySeconds = 60;

    private readonly IGpuClockOffsets _device;
    private readonly Action<string>? _log;
    private GpuOffsetSettings _settings;
    private GpuClockOffset? _owned;  // não-zero que o killer-sense gravou (precisa desfazer na saída)
    private GpuClockOffset? _current; // última leitura do driver
    private GpuClockOffset? _target;
    private OperatingMode? _lastMode;
    private bool _lastAllowed = true;
    private bool _lastOn;
    private bool _pending = true;
    private bool _hot;
    private double _retryIn;
    private string? _message;

    public GpuOffsetController(IGpuClockOffsets device, GpuOffsetSettings settings, Action<string>? log = null)
    {
        _device = device;
        _settings = settings;
        _log = log;
    }

    public GpuOffsetSettings Settings => _settings;

    /// <summary>Verdadeiro se há um offset não-zero gravado pelo killer-sense no driver.</summary>
    public bool OwnsOffset => _owned is not null;

    public GpuOffsetStatus Status => new(_settings.Enabled, _current, _target, _pending, _hot, _message);

    /// <summary>Nova configuração (já validada): reavalia no próximo ciclo com a GPU ligada.</summary>
    public void SetSettings(GpuOffsetSettings settings)
    {
        _settings = settings;
        _pending = true;
        _retryIn = 0;
        _message = null;
    }

    /// <summary>Depois de suspensão: confere de novo no próximo ciclo.</summary>
    public void Recheck() => _pending = true;

    /// <param name="dt">Segundos desde o ciclo anterior.</param>
    /// <param name="mode">Modo de operação lido do firmware (null = sem leitura).</param>
    /// <param name="gpu">Leitura da GPU deste ciclo (não acorda a GPU).</param>
    /// <param name="allowed">Falso com o killer-sense desativado ou suspenso.</param>
    public void Tick(double dt, OperatingMode? mode, GpuReading gpu, bool allowed)
    {
        if (mode != _lastMode || allowed != _lastAllowed)
        {
            _pending = true;
            _lastMode = mode;
            _lastAllowed = allowed;
        }

        if (gpu.PoweredOn && !_lastOn)
        {
            _pending = true; // religou: confere se o driver manteve o offset
        }

        _lastOn = gpu.PoweredOn;
        UpdateHot(gpu);

        if (_retryIn > 0)
        {
            _retryIn -= dt;
            if (_retryIn > 0)
            {
                return;
            }

            _pending = true;
        }

        _target = TargetFor(mode, allowed);
        if (!_pending || !gpu.PoweredOn)
        {
            return; // nada novo, ou a GPU está desligada: espera ela ligar sozinha
        }

        if (_target is not { } target)
        {
            _pending = false; // nada nosso no driver e nada a pôr: não toca
            return;
        }

        Ensure(target);
    }

    /// <summary>Saída do serviço: zera o que o killer-sense gravou (mesmo com a GPU desligada).</summary>
    public void Release(string reason)
    {
        if (_owned is null)
        {
            return;
        }

        if (_device.Apply(GpuClockOffset.Zero, out var failure))
        {
            _owned = null;
            _current = GpuClockOffset.Zero;
            _log?.Invoke($"zerado ({reason}).");
        }
        else
        {
            _log?.Invoke($"FALHA ao zerar ({reason}): {failure}. Reiniciar o PC desfaz.");
        }
    }

    private void UpdateHot(GpuReading gpu)
    {
        if (!gpu.PoweredOn)
        {
            return;
        }

        if (!_hot && (gpu.Temperature is not { } t || t >= HotCelsius))
        {
            _hot = true;
            _pending = true;
            if (_owned is not null)
            {
                _log?.Invoke(gpu.Temperature is { } hot
                    ? $"GPU a {hot:0} °C: offset retirado até esfriar abaixo de {CoolCelsius:0} °C."
                    : $"leitura inválida da GPU: offset retirado até uma leitura abaixo de {CoolCelsius:0} °C.");
            }
        }
        else if (_hot && gpu.Temperature is { } c && c < CoolCelsius)
        {
            _hot = false;
            _pending = true;
        }
    }

    private GpuClockOffset? TargetFor(OperatingMode? mode, bool allowed)
    {
        var wanted = allowed && _settings.Enabled && !_hot && mode is { } m ? _settings.For(m) : GpuClockOffset.Zero;
        return wanted.IsZero && _owned is null ? null : wanted;
    }

    private void Ensure(GpuClockOffset target)
    {
        if (_device.Read(out var failure) is not { } before)
        {
            Fail($"leitura do offset falhou: {failure}");
            return;
        }

        _current = before.Current;
        var safe = GpuOffsetPolicy.Clamp(target, before);
        if (before.Current != safe)
        {
            if (!_device.Apply(safe, out failure))
            {
                Fail(failure ?? "escrita recusada");
                return;
            }

            var after = _device.Read(out failure);
            _current = after?.Current;
            if (after?.Current != safe)
            {
                Fail(after is null ? $"leitura depois da escrita falhou: {failure}" : $"o driver ficou com {after.Current} em vez de {safe}");
                _owned = after is { Current.IsZero: false } ? after.Current : _owned; // continua responsável pelo que ficou
                return;
            }

            _log?.Invoke($"{before.Current} → {safe} (modo {_lastMode?.ToString() ?? "?"}).");
        }

        _owned = safe.IsZero ? null : safe;
        _pending = false;
        _message = null;
    }

    private void Fail(string message)
    {
        _message = message;
        _retryIn = RetrySeconds;
        _pending = false;
        _log?.Invoke($"{message}. Nova tentativa em {RetrySeconds:0} s.");
    }
}
