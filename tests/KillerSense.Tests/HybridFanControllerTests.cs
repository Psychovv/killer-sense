using KillerSense.Core;

namespace KillerSense.Tests;

public class HybridFanControllerTests
{
    private static readonly FanCurve Curve = FanCurve.Create([new(60, 0), new(80, 50), new(90, 100)]);

    private static HybridFanController Make(double rampDown = 100) =>
        new(Curve, new FanTuning { RampDownPercentPerSecond = rampDown }, entryTemperature: 60);

    [Fact]
    public void Abaixo_da_entrada_fica_no_auto_do_ec()
    {
        var h = Make();
        Assert.Equal(FanDecision.Auto, h.Step(55, OperatingMode.Balanced, 1));
        Assert.False(h.IsCurveActive);
    }

    [Fact]
    public void Na_entrada_a_curva_assume()
    {
        var h = Make();
        Assert.Equal(FanDecision.Custom(25), h.Step(70, OperatingMode.Balanced, 1));
        Assert.True(h.IsCurveActive);
    }

    [Fact]
    public void Continua_na_curva_ate_cair_8_graus_abaixo_da_entrada()
    {
        var h = Make();
        h.Step(70, OperatingMode.Balanced, 1);
        Assert.Equal(FanMode.Custom, h.Step(55, OperatingMode.Balanced, 1).Mode); // 5 °C abaixo da entrada
        Assert.Equal(FanDecision.Auto, h.Step(52, OperatingMode.Balanced, 1));   // 8 °C abaixo e no piso
        Assert.False(h.IsCurveActive);
    }

    [Fact]
    public void Nao_volta_ao_auto_enquanto_a_rampa_ainda_desce()
    {
        var h = Make(rampDown: 3);
        h.Step(80, OperatingMode.Balanced, 1); // 50%
        var d = h.Step(40, OperatingMode.Balanced, 1);
        Assert.Equal(FanDecision.Custom(47), d);
    }

    [Fact]
    public void Em_turbo_o_piso_de_30_vale_e_a_volta_ao_auto_espera_o_piso()
    {
        var h = Make();
        Assert.Equal(FanDecision.Custom(30), h.Step(62, OperatingMode.Turbo, 1));
        Assert.Equal(FanDecision.Auto, h.Step(50, OperatingMode.Turbo, 1));
    }

    [Fact]
    public void Reentra_na_curva_depois_de_voltar_ao_auto()
    {
        var h = Make();
        h.Step(70, OperatingMode.Balanced, 1);
        h.Step(50, OperatingMode.Balanced, 1);
        Assert.Equal(FanDecision.Custom(50), h.Step(80, OperatingMode.Balanced, 1)); // primeiro ciclo vai direto
    }

    [Theory]
    [InlineData(20)]
    [InlineData(95)]
    public void Temperatura_de_entrada_fora_da_faixa_e_rejeitada(double entry) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HybridFanController(Curve, new FanTuning(), entry));
}
