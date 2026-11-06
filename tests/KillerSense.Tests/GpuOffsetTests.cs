using KillerSense.Ipc;
using KillerSense.Core;

namespace KillerSense.Tests;

public class GpuOffsetTests
{
    private static readonly GpuReading On = GpuReading.Value(50);
    private static readonly GpuClockOffset Turbo = new(100, 200);

    private static GpuOffsetSettings Enabled() => GpuOffsetSettings.Default with { Enabled = true };

    private static (GpuOffsetController Ctl, SimulatedGpuClockOffsets Dev) Make(GpuOffsetSettings? s = null)
    {
        var dev = new SimulatedGpuClockOffsets();
        return (new GpuOffsetController(dev, s ?? Enabled()), dev);
    }

    [Fact]
    public void Padrao_e_desligado_e_nao_toca_no_driver()
    {
        var (ctl, dev) = Make(GpuOffsetSettings.Default);
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal(0, dev.Writes);
        Assert.Equal(0, dev.Reads);
        Assert.False(ctl.OwnsOffset);
    }

    [Fact]
    public void Turbo_aplica_o_padrao_da_Acer_e_confere_lendo_de_volta()
    {
        var (ctl, dev) = Make();
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal(Turbo, dev.Current);
        Assert.Equal(1, dev.Writes);
        Assert.Equal(2, dev.Reads); // antes e depois
        Assert.True(ctl.OwnsOffset);
        Assert.Equal(Turbo, ctl.Status.Current);
    }

    [Fact]
    public void Nao_regrava_a_cada_ciclo()
    {
        var (ctl, dev) = Make();
        for (var i = 0; i < 10; i++)
        {
            ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        }

        Assert.Equal(1, dev.Writes);
        Assert.Equal(2, dev.Reads);
    }

    [Fact]
    public void Trocar_de_modo_volta_a_zero()
    {
        var (ctl, dev) = Make();
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        ctl.Tick(1, OperatingMode.Balanced, On, allowed: true);
        Assert.Equal(GpuClockOffset.Zero, dev.Current);
        Assert.False(ctl.OwnsOffset);
    }

    [Fact]
    public void Nao_acorda_a_GPU_desligada_e_aplica_quando_ela_liga()
    {
        var (ctl, dev) = Make();
        ctl.Tick(1, OperatingMode.Turbo, GpuReading.Off, allowed: true);
        Assert.Equal(0, dev.Reads + dev.Writes);
        Assert.True(ctl.Status.Pending);
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal(Turbo, dev.Current);
    }

    [Fact]
    public void Ao_religar_confere_e_reaplica_se_o_driver_perdeu()
    {
        var (ctl, dev) = Make();
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        ctl.Tick(1, OperatingMode.Turbo, GpuReading.Off, allowed: true);
        dev.Current = GpuClockOffset.Zero; // driver esqueceu
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal(Turbo, dev.Current);
        Assert.Equal(2, dev.Writes);
    }

    [Fact]
    public void Nao_briga_com_offset_de_outro_programa()
    {
        var (ctl, dev) = Make();
        dev.Current = new GpuClockOffset(50, 0); // PredatorSense, Afterburner...
        ctl.Tick(1, OperatingMode.Balanced, On, allowed: true); // alvo zero, nada nosso
        Assert.Equal(new GpuClockOffset(50, 0), dev.Current);
        Assert.Equal(0, dev.Writes);
    }

    [Fact]
    public void Quente_retira_o_offset_ate_esfriar()
    {
        var (ctl, dev) = Make();
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        ctl.Tick(1, OperatingMode.Turbo, GpuReading.Value(GpuOffsetController.HotCelsius), allowed: true);
        Assert.Equal(GpuClockOffset.Zero, dev.Current);
        Assert.True(ctl.Status.Hot);
        ctl.Tick(1, OperatingMode.Turbo, GpuReading.Value(80), allowed: true); // ainda acima do limite de volta
        Assert.Equal(GpuClockOffset.Zero, dev.Current);
        ctl.Tick(1, OperatingMode.Turbo, GpuReading.Value(70), allowed: true);
        Assert.Equal(Turbo, dev.Current);
    }

    [Fact]
    public void Leitura_invalida_com_a_GPU_ligada_retira_o_offset()
    {
        var (ctl, dev) = Make();
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        ctl.Tick(1, OperatingMode.Turbo, GpuReading.Error, allowed: true);
        Assert.Equal(GpuClockOffset.Zero, dev.Current);
    }

    [Fact]
    public void Desativado_zera_e_Release_zera_na_saida()
    {
        var (ctl, dev) = Make();
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: false);
        Assert.Equal(GpuClockOffset.Zero, dev.Current);

        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal(Turbo, dev.Current);
        ctl.Release("teste");
        Assert.Equal(GpuClockOffset.Zero, dev.Current);
        Assert.False(ctl.OwnsOffset);
    }

    [Fact]
    public void Release_sem_nada_gravado_nao_toca_no_driver()
    {
        var (ctl, dev) = Make();
        dev.Current = new GpuClockOffset(50, 0);
        ctl.Release("teste");
        Assert.Equal(0, dev.Writes);
    }

    [Fact]
    public void Desligar_a_opcao_zera_o_que_foi_gravado()
    {
        var (ctl, dev) = Make();
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        ctl.SetSettings(GpuOffsetSettings.Default);
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal(GpuClockOffset.Zero, dev.Current);
        Assert.False(ctl.OwnsOffset);
    }

    [Fact]
    public void Falha_do_driver_espera_antes_de_tentar_de_novo()
    {
        var (ctl, dev) = Make();
        dev.FailWith = "recusado";
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal("recusado", ctl.Status.Message);
        ctl.Tick(30, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal(1, dev.Writes);
        dev.FailWith = null;
        ctl.Tick(31, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal(Turbo, dev.Current);
        Assert.Null(ctl.Status.Message);
    }

    [Fact]
    public void Driver_que_ignora_a_escrita_vira_erro()
    {
        var (ctl, dev) = Make();
        dev.IgnoreWrites = true;
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        Assert.Contains("ficou com", ctl.Status.Message, StringComparison.Ordinal);
        Assert.False(ctl.OwnsOffset);
    }

    [Fact]
    public void Limites_cortam_valores_altos_mesmo_vindos_da_configuracao()
    {
        var s = new GpuOffsetSettings(true, new Dictionary<OperatingMode, GpuClockOffset> { [OperatingMode.Turbo] = new(900, 2500) });
        Assert.NotNull(s.Validate());
        var (ctl, dev) = Make(s);
        ctl.Tick(1, OperatingMode.Turbo, On, allowed: true);
        Assert.Equal(new GpuClockOffset(GpuOffsetPolicy.CoreMaxMhz, GpuOffsetPolicy.MemoryMaxMhz), dev.Current);
    }

    [Fact]
    public void Eco_nunca_tem_offset()
    {
        var s = new GpuOffsetSettings(true, new Dictionary<OperatingMode, GpuClockOffset> { [OperatingMode.Eco] = new(100, 0) });
        Assert.NotNull(s.Validate());
        Assert.Equal(GpuClockOffset.Zero, s.For(OperatingMode.Eco));
        Assert.Null(GpuOffsetSettings.Default.Validate());
    }

    [Fact]
    public void Faixa_do_driver_tambem_limita()
    {
        var reading = new GpuOffsetReading(GpuClockOffset.Zero, new GpuOffsetRange(-50, 80), null);
        Assert.Equal(new GpuClockOffset(80, 300), GpuOffsetPolicy.Clamp(new GpuClockOffset(150, 400), reading));
    }
}

public class GpuOffsetIpcTests
{
    [Fact]
    public void Configuracao_vai_e_volta_pelo_pipe()
    {
        var settings = new GpuOffsetSettings(true, new Dictionary<OperatingMode, GpuClockOffset>
        {
            [OperatingMode.Balanced] = new(-50, 0),
            [OperatingMode.Turbo] = new(100, 200),
        });
        var line = IpcProtocol.Serialize(new IpcRequest(IpcRequestType.SetGpuOffsets, GpuOffsets: GpuOffsetSettingsDto.From(settings)));
        var back = IpcProtocol.ParseRequest(line)!.GpuOffsets!.TryToSettings(out var error);
        Assert.Null(error);
        Assert.True(back!.Enabled);
        Assert.Equal(new GpuClockOffset(100, 200), back.For(OperatingMode.Turbo));
        Assert.Equal(new GpuClockOffset(-50, 0), back.For(OperatingMode.Balanced));
        Assert.DoesNotContain("isZero", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Servico_recusa_valores_fora_da_faixa_e_modo_repetido()
    {
        var alto = new GpuOffsetSettingsDto(true, [new(OperatingMode.Turbo, 500, 0)]);
        Assert.Null(alto.TryToSettings(out var e1));
        Assert.NotNull(e1);
        var repetido = new GpuOffsetSettingsDto(true, [new(OperatingMode.Turbo, 0, 0), new(OperatingMode.Turbo, 10, 0)]);
        Assert.Null(repetido.TryToSettings(out _));
        Assert.Null(IpcProtocol.ParseRequest("""{"type":"SetGpuOffsets","gpuOffsets":{"enabled":true,"modes":[{"mode":"Turbo","coreMhz":"x"}]}}"""));
    }

    [Fact]
    public void Estado_publica_o_status_do_offset()
    {
        var status = new GpuOffsetStatus(true, new(100, 200), new(100, 200), false, false, null);
        var state = new ServiceState(IpcProtocol.Version, DateTime.UtcNow, EngineState.Curve, 60, 50, OperatingMode.Turbo,
            FanMode.Auto, null, null, FanMode.Auto, null, null, null, GpuOffset: status);
        var back = IpcProtocol.ParseMessage(IpcProtocol.Serialize(IpcMessage.ForState(state)))!.State!.GpuOffset;
        Assert.Equal(status, back);
    }

    [Fact]
    public void Store_sem_arquivo_e_desligado_e_grava_atomico()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ks-gpu-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new KillerSense.Service.GpuOffsetStore(Path.Combine(dir, "gpu-offsets.json"));
            Assert.False(store.Load(out var p0).Enabled);
            Assert.Null(p0);
            store.Save(GpuOffsetSettings.Default with { Enabled = true });
            var loaded = store.Load(out var p1);
            Assert.Null(p1);
            Assert.True(loaded.Enabled);
            Assert.Equal(new GpuClockOffset(100, 200), loaded.For(OperatingMode.Turbo));

            File.WriteAllText(Path.Combine(dir, "gpu-offsets.json"), """{"enabled":true,"modes":[{"mode":"Turbo","coreMhz":900,"memoryMhz":0}]}""");
            Assert.False(store.Load(out var p2).Enabled); // inválido ⇒ padrão desligado
            Assert.NotNull(p2);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
