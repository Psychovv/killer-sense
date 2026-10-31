namespace KillerSense.Core;

/// <summary>Estado geral do controle (mostrado na interface e no log).</summary>
public enum EngineState
{
    /// <summary>Todas as fans no Auto do EC (repouso ou curva ainda não assumiu).</summary>
    Auto,

    /// <summary>Ao menos uma fan sendo controlada pela curva.</summary>
    Curve,

    /// <summary>Temperatura de emergência: fans no Max até esfriar.</summary>
    Emergency,

    /// <summary>Sensor de temperatura em falha: fans no Auto, curva suspensa.</summary>
    SensorFault,

    /// <summary>Outro programa (ex.: PredatorSense) mudou o modo das fans: o controle pausou.</summary>
    ExternalOverride,

    /// <summary>Erro ao gravar no firmware: Auto restaurado e controle pausado.</summary>
    FirmwareFault,

    /// <summary>Pausado pelo sistema (suspensão) ou pelo usuário.</summary>
    Paused,
}

/// <summary>Perfil de fans usado pelo motor (o perfil completo, com JSON, vem depois).</summary>
public sealed record FanProfile(FanCurve CpuCurve, FanCurve GpuCurve, FanTuning Tuning, double CpuEntryTemperature, double GpuEntryTemperature)
{
    /// <summary>Perfil padrão do killer-sense (silencioso em repouso, assume a partir de 60/55 °C).</summary>
    public static FanProfile Default { get; } = new(
        FanCurve.Create([new(60, 0), new(70, 25), new(80, 50), new(88, 80), new(95, 100)]),
        FanCurve.Create([new(55, 0), new(65, 25), new(75, 50), new(82, 80), new(87, 100)]),
        new FanTuning(),
        SafetyPolicy.DefaultCpuEntryTemperature,
        SafetyPolicy.DefaultGpuEntryTemperature);
}

/// <summary>Resultado de um ciclo.</summary>
public sealed record EngineSnapshot(
    EngineState State,
    double? CpuTemperature,
    OperatingMode? OperatingMode,
    FanDecision Cpu,
    FanDecision Gpu,
    string? Message)
{
    /// <summary>Verdadeiro se alguma fan não está no Auto do EC por decisão do serviço (vale para o watchdog).</summary>
    public bool ManualActive => Cpu.Mode != FanMode.Auto || Gpu.Mode != FanMode.Auto;
}

/// <summary>
/// Um ciclo do controle de fans sobre um <see cref="IAcerFirmware"/>. Sem threads, sem relógio próprio:
/// o serviço chama <see cref="Tick"/> a cada segundo. Toda decisão de segurança está aqui para ser testável.
/// </summary>
public sealed class FanControlEngine
{
    private readonly IAcerFirmware _fw;
    private readonly HybridFanController _cpuCtl;
    private readonly HybridFanController _gpuCtl;
    private readonly SensorFilter _cpuTemp = SensorFilter.ForTemperature();
    private readonly EmergencyMonitor _emergency = new();
    private readonly Dictionary<Fan, FanMode> _applied = new() { [Fan.Cpu] = FanMode.Auto, [Fan.Gpu] = FanMode.Auto };
    private readonly Dictionary<Fan, int?> _appliedPercent = new() { [Fan.Cpu] = null, [Fan.Gpu] = null };
    private OperatingMode? _lastMode;
    private readonly Queue<double> _overrideTimes = new();
    private bool _paused;
    private EngineState? _pauseReason;
    private string? _pauseMessage;
    private double _clock;           // segundos acumulados pelos Ticks (não depende do relógio do sistema)
    private double _overrideAt;      // _clock da última mudança externa
    private bool _overrideLocked;    // mudanças externas demais: só retoma manualmente ou ao reiniciar

    public FanControlEngine(IAcerFirmware firmware, FanProfile profile)
    {
        ArgumentNullException.ThrowIfNull(firmware);
        ArgumentNullException.ThrowIfNull(profile);
        _fw = firmware;
        _cpuCtl = new HybridFanController(profile.CpuCurve, profile.Tuning, profile.CpuEntryTemperature);
        _gpuCtl = new HybridFanController(profile.GpuCurve, profile.Tuning, profile.GpuEntryTemperature);
    }

    /// <summary>Último resultado.</summary>
    public EngineSnapshot? Last { get; private set; }

    /// <summary>Coloca as duas fans no Auto do EC e confirma por leitura. Lança se não conseguir.</summary>
    public void RestoreAuto()
    {
        _fw.SetFanModes(new Dictionary<Fan, FanMode> { [Fan.Cpu] = FanMode.Auto, [Fan.Gpu] = FanMode.Auto });
        _applied[Fan.Cpu] = FanMode.Auto;
        _applied[Fan.Gpu] = FanMode.Auto;
        _appliedPercent[Fan.Cpu] = null;
        _appliedPercent[Fan.Gpu] = null;
        _cpuCtl.Reset();
        _gpuCtl.Reset();
        if (_fw.GetFanMode(Fan.Cpu) != FanMode.Auto || _fw.GetFanMode(Fan.Gpu) != FanMode.Auto)
        {
            throw new FirmwareException("O firmware não confirmou o Auto depois de SetGamingFanBehavior.");
        }
    }

    /// <summary>Pausa o controle (ex.: suspensão) já com as fans no Auto.</summary>
    public void Pause(string reason)
    {
        RestoreAuto();
        _paused = true;
        _pauseReason = EngineState.Paused;
        _pauseMessage = reason;
    }

    /// <summary>Retoma o controle depois de uma pausa (pedido manual: também libera o bloqueio de mudanças externas).</summary>
    public void Resume()
    {
        _paused = false;
        _pauseReason = null;
        _pauseMessage = null;
        _overrideLocked = false;
        _overrideTimes.Clear();
    }

    /// <param name="elapsedSeconds">Tempo desde o ciclo anterior.</param>
    /// <param name="gpuTemperature">Temperatura válida da GPU (NVML); null = sem leitura ⇒ fan da GPU no Auto do EC.</param>
    public EngineSnapshot Tick(double elapsedSeconds, double? gpuTemperature = null)
    {
        if (double.IsFinite(elapsedSeconds) && elapsedSeconds > 0)
        {
            _clock += Math.Min(elapsedSeconds, 60);
        }

        if (_paused && _pauseReason == EngineState.ExternalOverride && !_overrideLocked
            && _clock - _overrideAt >= SafetyPolicy.ExternalOverrideResumeSeconds)
        {
            // Retomada automática (§7): sempre recomeça do Auto.
            try
            {
                RestoreAuto();
                _paused = false;
                _pauseReason = null;
                _pauseMessage = null;
            }
            catch (FirmwareException ex)
            {
                return FailAndPause(EngineState.FirmwareFault, $"Erro ao retomar: {ex.Message}");
            }
        }

        if (_paused)
        {
            return Publish(_pauseReason ?? EngineState.Paused, _pauseMessage);
        }

        try
        {
            return TickCore(elapsedSeconds, gpuTemperature);
        }
        catch (FirmwareException ex)
        {
            return FailAndPause(EngineState.FirmwareFault, $"Erro no firmware: {ex.Message}");
        }
    }

    private EngineSnapshot TickCore(double dt, double? gpuTemperature)
    {
        _cpuTemp.Add(TryRead(() => _fw.ReadSensor(Sensor.CpuTemperature)));
        var mode = TryReadMode() ?? _lastMode;
        _lastMode = mode;
        var floorMode = mode ?? OperatingMode.Turbo; // sem leitura do modo: usa o piso mais alto

        // 1) Sensor em falha → Auto do EC.
        if (_cpuTemp.IsFaulted)
        {
            EnsureAllAuto();
            return Publish(EngineState.SensorFault, $"{SafetyPolicy.MaxConsecutiveInvalidReadings} leituras inválidas seguidas da temperatura da CPU.");
        }

        var temp = _cpuTemp.Value;
        if (temp is not { } t)
        {
            EnsureAllAuto(); // ainda sem leitura válida
            return Publish(EngineState.Auto, null);
        }

        // 2) Emergência → Max (única situação em que o serviço usa Max).
        var wasEmergency = _emergency.IsActive;
        if (_emergency.Update(t, gpuTemperature))
        {
            if (_applied[Fan.Cpu] != FanMode.Max || _applied[Fan.Gpu] != FanMode.Max)
            {
                ApplyModes(new() { [Fan.Cpu] = FanMode.Max, [Fan.Gpu] = FanMode.Max });
                _cpuCtl.Reset();
                _gpuCtl.Reset();
            }

            return Publish(EngineState.Emergency, $"Temperatura de emergência: CPU {t:0} °C.");
        }

        if (wasEmergency)
        {
            EnsureAllAuto();
            return Publish(EngineState.Auto, "Emergência encerrada; fans de volta ao Auto.");
        }

        // 3) Mudança feita por fora (PredatorSense, tecla, etc.): não brigar.
        foreach (var fan in new[] { Fan.Cpu, Fan.Gpu })
        {
            if (_applied[fan] == FanMode.Custom && _fw.GetFanMode(fan) != FanMode.Custom)
            {
                _cpuCtl.Reset();
                _gpuCtl.Reset();
                _applied[Fan.Cpu] = _fw.GetFanMode(Fan.Cpu);
                _applied[Fan.Gpu] = _fw.GetFanMode(Fan.Gpu);
                while (_overrideTimes.Count > 0 && _clock - _overrideTimes.Peek() > SafetyPolicy.ExternalOverrideWindowSeconds)
                {
                    _overrideTimes.Dequeue();
                }

                _overrideTimes.Enqueue(_clock);
                _overrideAt = _clock;
                _overrideLocked = _overrideTimes.Count > SafetyPolicy.MaxExternalOverrides;
                _paused = true;
                _pauseReason = EngineState.ExternalOverride;
                _pauseMessage = _overrideLocked
                    ? $"O modo da fan {fan} foi alterado por outro programa {_overrideTimes.Count} vezes em {SafetyPolicy.ExternalOverrideWindowSeconds / 60:0} min; controle pausado até retomar manualmente ou reiniciar o serviço."
                    : $"O modo da fan {fan} foi alterado por outro programa; controle pausado, retomada automática em {SafetyPolicy.ExternalOverrideResumeSeconds:0} s.";
                return Publish(EngineState.ExternalOverride, _pauseMessage);
            }
        }

        // 4) Curva híbrida por fan. Sem leitura da GPU (NVML ainda não integrado, ou dGPU desligada pelo
        //    Optimus), a fan da GPU fica no Auto do EC — que tem a proteção do próprio firmware.
        var cpu = _cpuCtl.Step(t, floorMode, dt);
        var gpu = gpuTemperature is { } g ? _gpuCtl.Step(g, floorMode, dt) : FanDecision.Auto;
        if (gpuTemperature is null)
        {
            _gpuCtl.Reset();
        }
        Apply(Fan.Cpu, cpu, Fan.Gpu, gpu);
        var state = cpu.Mode == FanMode.Custom || gpu.Mode == FanMode.Custom ? EngineState.Curve : EngineState.Auto;
        return Publish(state, null);
    }

    private void Apply(Fan a, FanDecision da, Fan b, FanDecision db)
    {
        // % primeiro (o EC guarda o % mesmo em Auto), depois o modo numa única chamada.
        foreach (var (fan, d) in new[] { (a, da), (b, db) })
        {
            if (d.Mode == FanMode.Custom && d.Percent is { } p && _appliedPercent[fan] != p)
            {
                _fw.SetFanSpeedPercent(fan, p);
                _appliedPercent[fan] = p;
            }
        }

        var changes = new Dictionary<Fan, FanMode>();
        foreach (var (fan, d) in new[] { (a, da), (b, db) })
        {
            if (_applied[fan] != d.Mode)
            {
                changes[fan] = d.Mode;
            }
        }

        if (changes.Count > 0)
        {
            ApplyModes(changes);
        }
    }

    private void ApplyModes(Dictionary<Fan, FanMode> modes)
    {
        _fw.SetFanModes(modes);
        foreach (var (fan, mode) in modes)
        {
            _applied[fan] = mode;
            if (mode != FanMode.Custom)
            {
                _appliedPercent[fan] = null;
            }
        }
    }

    private void EnsureAllAuto()
    {
        if (_applied[Fan.Cpu] != FanMode.Auto || _applied[Fan.Gpu] != FanMode.Auto)
        {
            RestoreAuto();
        }

        _cpuCtl.Reset();
        _gpuCtl.Reset();
    }

    private EngineSnapshot FailAndPause(EngineState state, string message)
    {
        string full = message;
        try
        {
            RestoreAuto();
        }
        catch (FirmwareException ex)
        {
            full += $" | Falha ao restaurar o Auto: {ex.Message}";
        }

        _paused = true;
        _pauseReason = state;
        _pauseMessage = full;
        return Publish(state, full);
    }

    private EngineSnapshot Publish(EngineState state, string? message)
    {
        Last = new EngineSnapshot(
            state,
            _cpuTemp.Value,
            _lastMode,
            new FanDecision(_applied[Fan.Cpu], _appliedPercent[Fan.Cpu]),
            new FanDecision(_applied[Fan.Gpu], _appliedPercent[Fan.Gpu]),
            message);
        return Last;
    }

    private OperatingMode? TryReadMode()
    {
        try
        {
            return _fw.GetOperatingMode();
        }
        catch (FirmwareException)
        {
            return null;
        }
    }

    private static double? TryRead(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (FirmwareException)
        {
            return null;
        }
    }
}
