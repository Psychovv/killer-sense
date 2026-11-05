using KillerSense.Core;
using KillerSense.Firmware;

namespace KillerSense.Tests;

public class FanCurveTests
{
    private static FanCurve Default() => FanCurve.Create([new(40, 25), new(55, 32), new(70, 50), new(82, 75), new(92, 100)]);

    [Theory]
    [InlineData(30, 25)]
    [InlineData(40, 25)]
    [InlineData(62.5, 41)]
    [InlineData(92, 100)]
    [InlineData(105, 100)]
    public void Interpola_e_satura_nas_pontas(double t, double expected) =>
        Assert.Equal(expected, Default().Evaluate(t), 3);

    [Fact]
    public void Rejeita_curvas_invalidas()
    {
        Assert.NotNull(FanCurve.Validate([new(50, 30)]));                          // 1 ponto
        Assert.NotNull(FanCurve.Validate([new(50, 30), new(50, 40)]));             // temp repetida
        Assert.NotNull(FanCurve.Validate([new(50, 60), new(70, 40)]));             // % descendo
        Assert.NotNull(FanCurve.Validate([new(50, 30), new(70, 140)]));            // > 100%
        Assert.NotNull(FanCurve.Validate([new(0, 30), new(70, 40)]));              // temp 0
        Assert.NotNull(FanCurve.Validate([new(50, double.NaN), new(70, 40)]));     // NaN
        Assert.Null(FanCurve.Validate([new(50, 30), new(70, 30)]));                // plano é válido
    }

    [Fact]
    public void Novo_ponto_fica_em_cima_da_curva()
    {
        var pts = Default().Points;
        var added = FanCurve.WithPointAt(pts, 62.4); // arredonda para 62 °C
        Assert.NotNull(added);
        Assert.Equal(6, added.Length);
        Assert.Equal(new CurvePoint(62, 40), added[2]); // 32 + (50 − 32) × 7/15 = 40,4 → 40
        Assert.Equal(Default().Evaluate(62), FanCurve.Create(added).Evaluate(62), 0);
        Assert.Null(FanCurve.Validate(added));
    }

    [Fact]
    public void Novo_ponto_nas_pontas_repete_a_ponta()
    {
        var pts = Default().Points;
        Assert.Equal(new CurvePoint(32, 25), FanCurve.WithPointAt(pts, 32)![0]);
        Assert.Equal(new CurvePoint(98, 100), FanCurve.WithPointAt(pts, 98)![^1]);
    }

    [Fact]
    public void Nao_adiciona_ponto_repetido_nem_acima_do_limite()
    {
        var pts = Default().Points;
        Assert.Null(FanCurve.WithPointAt(pts, 55.3)); // já existe 55 °C
        var full = Enumerable.Range(0, FanCurve.MaxPoints).Select(i => new CurvePoint(30 + (i * 7), 20 + (i * 8))).ToArray();
        Assert.Null(FanCurve.Validate(full));
        Assert.Null(FanCurve.WithPointAt(full, 33)); // já tem o máximo
    }
}

public class FanCurveControllerTests
{
    private static FanCurveController Make(FanTuning? tuning = null) =>
        new(FanCurve.Create([new(40, 0), new(60, 50), new(80, 100)]), tuning ?? new FanTuning());

    [Theory]
    [InlineData(OperatingMode.Quiet, 0)]
    [InlineData(OperatingMode.Eco, 0)]
    [InlineData(OperatingMode.Balanced, 0)]
    [InlineData(OperatingMode.Performance, 15)]
    [InlineData(OperatingMode.Turbo, 30)]
    public void Nunca_fica_abaixo_do_piso_do_modo(OperatingMode mode, int floor) =>
        Assert.Equal(floor, Make().Step(30, mode, 1));

    [Fact]
    public void Piso_do_usuario_prevalece_se_for_maior() =>
        Assert.Equal(20, Make(new FanTuning { MinPercent = 20 }).Step(30, OperatingMode.Balanced, 1));

    [Fact]
    public void Primeiro_ciclo_vai_direto_ao_alvo() => Assert.Equal(50, Make().Step(60, OperatingMode.Balanced, 1));

    [Fact]
    public void Sobe_limitado_pela_rampa_de_subida()
    {
        var c = Make();
        Assert.Equal(0, c.Step(40, OperatingMode.Balanced, 1));
        Assert.Equal(15, c.Step(80, OperatingMode.Balanced, 1)); // 0 + 15
        Assert.Equal(30, c.Step(80, OperatingMode.Balanced, 1));
    }

    [Fact]
    public void Desce_devagar_pela_rampa_de_descida()
    {
        var c = Make();
        Assert.Equal(100, c.Step(80, OperatingMode.Balanced, 1));
        Assert.Equal(97, c.Step(40, OperatingMode.Balanced, 1)); // −3 %/s
    }

    [Fact]
    public void Histerese_segura_a_descida_ate_cair_3_graus()
    {
        var c = Make(new FanTuning { RampDownPercentPerSecond = 100 });
        Assert.Equal(50, c.Step(60, OperatingMode.Balanced, 1));
        Assert.Equal(50, c.Step(58, OperatingMode.Balanced, 1)); // caiu 2 °C: não muda
        Assert.Equal(48, c.Step(56, OperatingMode.Balanced, 1)); // caiu 4 °C: usa 56 + 3 = 59 °C
    }

    [Fact]
    public void Troca_para_turbo_aplica_o_piso_na_hora_mesmo_com_rampa()
    {
        var c = Make();
        Assert.Equal(0, c.Step(30, OperatingMode.Balanced, 1));
        Assert.Equal(30, c.Step(30, OperatingMode.Turbo, 1)); // rampa daria 15; o piso força 30
    }

    [Fact]
    public void Tuning_invalido_e_rejeitado() =>
        Assert.Throws<ArgumentException>(() => Make(new FanTuning { MinPercent = 80, MaxPercent = 50 }));
}

public class SensorFilterTests
{
    [Fact]
    public void Temperatura_zero_e_invalida_e_tres_seguidas_viram_falha()
    {
        var f = SensorFilter.ForTemperature();
        Assert.True(f.Add(61));
        Assert.False(f.Add(0));
        Assert.False(f.Add(0));
        Assert.Equal(61, f.Value);
        Assert.False(f.Add(null)); // erro de leitura
        Assert.True(f.IsFaulted);
        Assert.Null(f.Value);
    }

    [Fact]
    public void Mediana_das_tres_ultimas_ignora_um_pico()
    {
        var f = SensorFilter.ForTemperature();
        f.Add(60);
        f.Add(99);
        f.Add(61);
        Assert.Equal(61, f.Value);
    }

    [Fact]
    public void Rpm_zero_e_valido_mas_acima_de_8000_nao()
    {
        var f = SensorFilter.ForFanRpm();
        Assert.True(f.Add(0));
        Assert.False(f.Add(9000));
    }

    [Fact]
    public void Leitura_valida_zera_o_contador_de_falhas()
    {
        var f = SensorFilter.ForTemperature();
        f.Add(0);
        f.Add(0);
        f.Add(55);
        Assert.Equal(0, f.ConsecutiveInvalid);
    }
}

public class EmergencyMonitorTests
{
    [Fact]
    public void Entra_com_cpu_95_e_so_sai_abaixo_de_85()
    {
        var m = new EmergencyMonitor();
        Assert.False(m.Update(94, 60));
        Assert.True(m.Update(95, 60));
        Assert.True(m.Update(90, 60));
        Assert.False(m.Update(85, 60));
    }

    [Fact]
    public void Entra_com_gpu_87()
    {
        var m = new EmergencyMonitor();
        Assert.True(m.Update(70, 87));
    }

    [Fact]
    public void Sem_leitura_da_cpu_nao_libera_emergencia()
    {
        var m = new EmergencyMonitor();
        m.Update(96, null);
        Assert.True(m.Update(null, null));
    }

    [Fact]
    public void Gpu_desligada_nao_impede_a_saida()
    {
        var m = new EmergencyMonitor();
        m.Update(96, null);
        Assert.False(m.Update(80, null));
    }
}

public class SimulatedFirmwareTests
{
    [Fact]
    public void Custom_guarda_percentual_e_volta_ao_auto_mantem_o_percentual()
    {
        var fw = new SimulatedFirmware();
        fw.SetFanModes(new Dictionary<Fan, FanMode> { [Fan.Cpu] = FanMode.Custom });
        fw.SetFanSpeedPercent(Fan.Cpu, 40);
        Assert.Equal(FanMode.Custom, fw.GetFanMode(Fan.Cpu));
        fw.SetFanModes(new Dictionary<Fan, FanMode> { [Fan.Cpu] = FanMode.Auto });
        Assert.Equal(40, fw.GetFanSpeedPercent(Fan.Cpu)); // igual ao snapshot 04
        Assert.Contains("SetFanModes(0x00010001)", fw.Calls);
    }

    [Fact]
    public void Falha_simulada_vira_FirmwareException()
    {
        var fw = new SimulatedFirmware { FailNextCall = "ReadSensor" };
        Assert.Throws<FirmwareException>(() => fw.ReadSensor(Sensor.CpuTemperature));
        Assert.Equal(55, fw.ReadSensor(Sensor.CpuTemperature));
    }
}
