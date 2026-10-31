using KillerSense.Core;
using KillerSense.Firmware;

namespace KillerSense.Tests;

public class GpuTemperatureFeedTests
{
    [Fact]
    public void Gpu_desligada_nao_conta_como_falha()
    {
        var f = new GpuTemperatureFeed();
        for (var i = 0; i < 10; i++)
        {
            Assert.Null(f.Update(GpuReading.Off));
        }

        Assert.False(f.IsFaulted);
    }

    [Fact]
    public void Tres_erros_com_a_gpu_ligada_viram_falha()
    {
        var f = new GpuTemperatureFeed();
        f.Update(GpuReading.Value(60));
        f.Update(GpuReading.Error);
        f.Update(GpuReading.Error);
        f.Update(GpuReading.Error);
        Assert.True(f.IsFaulted);
        Assert.Null(f.Update(GpuReading.Error));
    }

    [Fact]
    public void Ao_religar_recomeca_sem_historico()
    {
        var f = new GpuTemperatureFeed();
        f.Update(GpuReading.Value(80));
        f.Update(GpuReading.Value(80));
        f.Update(GpuReading.Off);
        Assert.Equal(50, f.Update(GpuReading.Value(50)));
    }

    [Fact]
    public void Leitura_zero_com_a_gpu_ligada_e_invalida()
    {
        var f = new GpuTemperatureFeed();
        Assert.Null(f.Update(GpuReading.Value(0)));
    }

    [Fact]
    public void Motor_usa_a_curva_da_gpu_quando_ela_esta_ligada()
    {
        var fw = new SimulatedFirmware();
        fw.SetSensor(Sensor.CpuTemperature, 50);
        var e = new FanControlEngine(fw, FanProfile.Default);
        var feed = new GpuTemperatureFeed();
        var s = e.Tick(1, feed.Update(GpuReading.Value(75)));
        Assert.Equal(FanDecision.Auto, s.Cpu);
        Assert.Equal(FanDecision.Custom(50), s.Gpu);
        Assert.Equal(75, s.GpuTemperature);
        s = e.Tick(1, feed.Update(GpuReading.Off));
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Gpu)); // desligou ⇒ Auto do EC
    }
}
