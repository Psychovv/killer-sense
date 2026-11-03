using KillerSense.Core;
using KillerSense.Firmware;
using KillerSense.Ipc;

namespace KillerSense.Tests;

/// <summary>Troca de modo pelo motor (A16) e leitura do modo durante as pausas.</summary>
public class OperatingModeTests
{
    private static (SimulatedFirmware Fw, FanControlEngine Engine) Make()
    {
        var fw = new SimulatedFirmware();
        fw.SetSensor(Sensor.CpuTemperature, 50);
        return (fw, new FanControlEngine(fw, FanProfile.Default));
    }

    [Fact]
    public void Troca_o_modo_e_confirma_por_leitura()
    {
        var (fw, e) = Make();
        e.SetOperatingMode(OperatingMode.Turbo, onAc: true);
        Assert.Equal(OperatingMode.Turbo, fw.OperatingMode);
        Assert.Contains("SetOperatingMode(0x50B)", fw.Calls);
        Assert.Equal(OperatingMode.Turbo, e.Tick(1).OperatingMode);
    }

    [Theory]
    [InlineData(OperatingMode.Turbo, false)]
    [InlineData(OperatingMode.Performance, false)]
    [InlineData(OperatingMode.Eco, true)]
    public void Recusa_modo_que_nao_existe_na_fonte_de_energia_atual(OperatingMode mode, bool onAc)
    {
        var (fw, e) = Make();
        Assert.Throws<InvalidOperationException>(() => e.SetOperatingMode(mode, onAc));
        Assert.DoesNotContain(fw.Calls, c => c.StartsWith("SetOperatingMode", StringComparison.Ordinal));
    }

    [Fact]
    public void Recusa_sem_saber_a_fonte_de_energia()
    {
        var (fw, e) = Make();
        Assert.Throws<InvalidOperationException>(() => e.SetOperatingMode(OperatingMode.Quiet, onAc: null));
        Assert.DoesNotContain(fw.Calls, c => c.StartsWith("SetOperatingMode", StringComparison.Ordinal));
    }

    [Fact]
    public void Desativado_nao_troca_o_modo()
    {
        var (fw, e) = Make();
        e.Disable("teste");
        Assert.Throws<InvalidOperationException>(() => e.SetOperatingMode(OperatingMode.Quiet, onAc: true));
        Assert.Equal(OperatingMode.Balanced, fw.OperatingMode);
    }

    [Fact]
    public void Pausado_pelo_usuario_continua_lendo_o_modo()
    {
        var (fw, e) = Make();
        e.Tick(1);
        e.Pause("Voltar ao Auto");
        fw.OperatingMode = OperatingMode.Turbo; // tecla Predator / PredatorSense
        Assert.Equal(OperatingMode.Turbo, e.Tick(1).OperatingMode);
    }

    [Fact]
    public void Suspenso_e_desativado_nao_chamam_o_firmware()
    {
        foreach (var pause in new Action<FanControlEngine>[] { x => x.Pause("suspensão", readMode: false), x => x.Disable("encerrado") })
        {
            var (fw, e) = Make();
            e.Tick(1);
            pause(e);
            var before = fw.Calls.Count;
            fw.OperatingMode = OperatingMode.Turbo;
            var s = e.Tick(1);
            Assert.Equal(before, fw.Calls.Count);
            Assert.Equal(OperatingMode.Balanced, s.OperatingMode); // último lido
        }
    }

    [Fact]
    public void Pedido_de_modo_ida_e_volta_no_json()
    {
        var line = IpcProtocol.Serialize(new IpcRequest(IpcRequestType.SetOperatingMode, Mode: OperatingMode.Turbo));
        Assert.Contains("\"mode\":\"Turbo\"", line, StringComparison.Ordinal);
        var back = IpcProtocol.ParseRequest(line);
        Assert.Equal(IpcRequestType.SetOperatingMode, back?.Type);
        Assert.Equal(OperatingMode.Turbo, back?.Mode);
    }
}
