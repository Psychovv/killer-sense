using KillerSense.Core;
using KillerSense.Firmware;

namespace KillerSense.Tests;

public class FanControlEngineTests
{
    private static (SimulatedFirmware Fw, FanControlEngine Engine) Make(int cpuTemp)
    {
        var fw = new SimulatedFirmware();
        fw.SetSensor(Sensor.CpuTemperature, cpuTemp);
        return (fw, new FanControlEngine(fw, FanProfile.Default));
    }

    private static bool Writes(SimulatedFirmware fw) => fw.Calls.Any(c => c.StartsWith("Set", StringComparison.Ordinal));

    [Fact]
    public void Em_repouso_fica_no_auto_e_nao_grava_nada()
    {
        var (fw, e) = Make(55);
        var s = e.Tick(1);
        Assert.Equal(EngineState.Auto, s.State);
        Assert.False(s.ManualActive);
        Assert.False(Writes(fw));
    }

    [Fact]
    public void Acima_da_entrada_grava_o_percentual_e_depois_o_modo_e_a_gpu_sem_leitura_fica_no_auto()
    {
        var (fw, e) = Make(75);
        var s = e.Tick(1);
        Assert.Equal(EngineState.Curve, s.State);
        Assert.Equal(FanDecision.Custom(38), s.Cpu); // CPU: 70→25, 80→50 ⇒ 37,5 ⇒ 38
        Assert.Equal(FanDecision.Auto, s.Gpu);
        var sets = fw.Calls.Where(c => c.StartsWith("Set", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["SetFanSpeedPercent(Cpu,38)", "SetFanModes(0x00030001)"], sets);
        Assert.True(s.ManualActive);
    }

    [Fact]
    public void Com_leitura_da_gpu_as_duas_curvas_assumem_numa_chamada()
    {
        var (fw, e) = Make(75);
        var s = e.Tick(1, gpuTemperature: 75);
        Assert.Equal(FanDecision.Custom(50), s.Gpu); // GPU: 75→50
        var sets = fw.Calls.Where(c => c.StartsWith("Set", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["SetFanSpeedPercent(Cpu,38)", "SetFanSpeedPercent(Gpu,50)", "SetFanModes(0x00C30009)"], sets);
    }

    [Fact]
    public void Gpu_em_87_graus_dispara_emergencia()
    {
        var (fw, e) = Make(70);
        Assert.Equal(EngineState.Emergency, e.Tick(1, gpuTemperature: 87).State);
        Assert.Equal(FanMode.Max, fw.GetFanMode(Fan.Gpu));
    }

    [Fact]
    public void Nao_regrava_quando_nada_muda()
    {
        var (fw, e) = Make(75);
        e.Tick(1);
        var before = fw.Calls.Count(c => c.StartsWith("Set", StringComparison.Ordinal));
        e.Tick(1);
        Assert.Equal(before, fw.Calls.Count(c => c.StartsWith("Set", StringComparison.Ordinal)));
    }

    [Fact]
    public void Tres_leituras_invalidas_voltam_ao_auto()
    {
        var (fw, e) = Make(75);
        e.Tick(1);
        fw.SetSensor(Sensor.CpuTemperature, 0);
        e.Tick(1);
        e.Tick(1);
        var s = e.Tick(1);
        Assert.Equal(EngineState.SensorFault, s.State);
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Cpu));
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Gpu));
    }

    [Fact]
    public void Erro_de_leitura_conta_como_invalido()
    {
        var (fw, e) = Make(75);
        e.Tick(1);
        for (var i = 0; i < 3; i++)
        {
            fw.FailNextCall = "ReadSensor";
            e.Tick(1);
        }

        Assert.Equal(EngineState.SensorFault, e.Last!.State);
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Cpu));
    }

    [Fact]
    public void Emergencia_poe_max_e_so_libera_abaixo_de_85()
    {
        var (fw, e) = Make(96);
        Assert.Equal(EngineState.Emergency, e.Tick(1).State);
        Assert.Equal(FanMode.Max, fw.GetFanMode(Fan.Cpu));
        fw.SetSensor(Sensor.CpuTemperature, 88);
        Assert.Equal(EngineState.Emergency, e.Tick(1).State);
        fw.SetSensor(Sensor.CpuTemperature, 84);
        Assert.Equal(EngineState.Emergency, e.Tick(1).State); // mediana [96, 88, 84] = 88
        var released = e.Tick(1);                            // mediana [88, 84, 84] = 84 ⇒ sai
        Assert.Equal(EngineState.Auto, released.State);
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Cpu));
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Gpu));
        Assert.Equal(EngineState.Curve, e.Tick(1).State);    // 84 °C ainda está acima da entrada (60)
    }

    [Fact]
    public void Mudanca_externa_pausa_sem_brigar()
    {
        var (fw, e) = Make(75);
        e.Tick(1);
        fw.SetFanModes(new Dictionary<Fan, FanMode> { [Fan.Cpu] = FanMode.Auto, [Fan.Gpu] = FanMode.Auto }); // "PredatorSense"
        var writesBefore = fw.Calls.Count(c => c.StartsWith("Set", StringComparison.Ordinal));
        Assert.Equal(EngineState.ExternalOverride, e.Tick(1).State);
        Assert.Equal(EngineState.ExternalOverride, e.Tick(1).State);
        Assert.Equal(writesBefore, fw.Calls.Count(c => c.StartsWith("Set", StringComparison.Ordinal)));
        e.Resume();
        Assert.Equal(EngineState.Curve, e.Tick(1).State);
    }

    private static readonly Dictionary<Fan, FanMode> ExternalAuto = new() { [Fan.Cpu] = FanMode.Auto, [Fan.Gpu] = FanMode.Auto };

    private static EngineSnapshot TickFor(FanControlEngine e, int seconds)
    {
        EngineSnapshot s = null!;
        for (var i = 0; i < seconds; i++)
        {
            s = e.Tick(1);
        }

        return s;
    }

    [Fact]
    public void Retoma_automaticamente_30s_depois_da_mudanca_externa_comecando_do_auto()
    {
        var (fw, e) = Make(75);
        e.Tick(1);
        fw.SetFanModes(ExternalAuto);
        Assert.Equal(EngineState.ExternalOverride, e.Tick(1).State);
        Assert.Equal(EngineState.ExternalOverride, TickFor(e, 29).State);
        var autoWritesBefore = fw.Calls.Count(c => c == "SetFanModes(0x00410009)");
        var resumed = e.Tick(1);
        Assert.Equal(EngineState.Curve, resumed.State);
        Assert.Equal(autoWritesBefore + 1, fw.Calls.Count(c => c == "SetFanModes(0x00410009)")); // recomeçou do Auto
        Assert.Equal(FanMode.Custom, fw.GetFanMode(Fan.Cpu));
    }

    [Fact]
    public void Quatro_mudancas_em_10_min_deixam_pausado_ate_retomar_manualmente()
    {
        var (fw, e) = Make(75);
        e.Tick(1);
        for (var i = 1; i <= 4; i++)
        {
            fw.SetFanModes(ExternalAuto);
            var s = e.Tick(1);
            Assert.Equal(EngineState.ExternalOverride, s.State);
            if (i < 4)
            {
                Assert.Equal(EngineState.Curve, TickFor(e, 30).State);
            }
        }

        Assert.Equal(EngineState.ExternalOverride, TickFor(e, 120).State);
        Assert.Contains("retomar manualmente", e.Last!.Message, StringComparison.Ordinal);
        e.Resume();
        Assert.Equal(EngineState.Curve, e.Tick(1).State);
    }

    [Fact]
    public void Mudancas_espacadas_continuam_retomando()
    {
        var (fw, e) = Make(75);
        e.Tick(1);
        for (var i = 0; i < 6; i++)
        {
            fw.SetFanModes(ExternalAuto);
            Assert.Equal(EngineState.ExternalOverride, e.Tick(1).State);
            Assert.Equal(EngineState.Curve, TickFor(e, 240).State); // uma mudança a cada ~4 min
        }
    }

    [Fact]
    public void Erro_ao_gravar_restaura_o_auto_e_pausa()
    {
        var (fw, e) = Make(75);
        fw.FailNextCall = "SetFanSpeedPercent";
        var s = e.Tick(1);
        Assert.Equal(EngineState.FirmwareFault, s.State);
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Cpu));
        Assert.Equal(EngineState.FirmwareFault, e.Tick(1).State);
    }

    [Fact]
    public void Pausa_restaura_o_auto()
    {
        var (fw, e) = Make(75);
        e.Tick(1);
        e.Pause("suspensão");
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Cpu));
        Assert.Equal(EngineState.Paused, e.Tick(1).State);
    }

    [Fact]
    public void Volta_ao_auto_quando_esfria()
    {
        var (fw, e) = Make(75);
        e.Tick(1);
        fw.SetSensor(Sensor.CpuTemperature, 45);
        for (var i = 0; i < 60; i++)
        {
            e.Tick(1);
        }

        Assert.Equal(EngineState.Auto, e.Last!.State);
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Cpu));
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Gpu));
    }
}

public class HeartbeatTests
{
    [Fact]
    public void Escreve_e_le_o_batimento()
    {
        var name = $@"Local\KillerSense.Test.{Guid.NewGuid():N}";
        using var writer = Heartbeat.CreateOrOpen(name);
        using var reader = Heartbeat.TryOpen(name)!;
        var now = new DateTime(2026, 11, 5, 22, 0, 0, DateTimeKind.Utc);
        writer.Beat(now, manualActive: true);
        Assert.Equal((now, true), reader.Read());
        reader.ClearManual();
        Assert.False(writer.Read().ManualActive);
    }

    [Fact]
    public void Sem_servico_nao_abre() => Assert.Null(Heartbeat.TryOpen($@"Local\KillerSense.Test.{Guid.NewGuid():N}"));
}
