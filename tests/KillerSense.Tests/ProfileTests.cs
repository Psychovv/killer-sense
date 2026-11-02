using KillerSense.Core;
using KillerSense.Firmware;
using KillerSense.Ipc;
using KillerSense.Service;

namespace KillerSense.Tests;

public class FanProfileDtoTests
{
    private static FanProfileDto Valid() => FanProfileDto.From(FanProfile.Default);

    [Fact]
    public void Perfil_padrao_vai_e_volta_pelo_dto_e_pelo_json()
    {
        var json = IpcProtocol.SerializeProfile(Valid());
        var back = IpcProtocol.ParseProfile(json)!.TryToProfile(out var error);
        Assert.Null(error);
        Assert.Equal(FanProfile.Default.CpuEntryTemperature, back!.CpuEntryTemperature);
        Assert.Equal(FanProfile.Default.GpuCurve.Points, back.GpuCurve.Points);
    }

    [Fact]
    public void Curva_descendo_e_recusada_com_o_motivo()
    {
        var bad = Valid() with { CpuCurve = [new(50, 60), new(70, 40)] };
        Assert.Null(bad.TryToProfile(out var error));
        Assert.StartsWith("Curva da CPU:", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(91)]
    [InlineData(double.NaN)]
    public void Entrada_fora_de_30_a_90_e_recusada(double entry)
    {
        Assert.Null((Valid() with { GpuEntryTemperature = entry }).TryToProfile(out var error));
        Assert.Contains("GPU", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rampa_invalida_e_recusada() =>
        Assert.Null((Valid() with { RampDownPercentPerSecond = 0 }).TryToProfile(out _));

    [Fact]
    public void Curva_ausente_no_json_e_recusada()
    {
        var dto = IpcProtocol.ParseProfile("{\"cpuEntryTemperature\":60}");
        Assert.Null(dto?.TryToProfile(out _));
    }
}

public class ProfileStoreTests
{
    [Fact]
    public void Salva_carrega_e_mantem_backup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ks-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "config.json");
        try
        {
            var store = new ProfileStore(path);
            Assert.Equal(FanProfile.Default, store.Load(out var p0)); // ausente ⇒ padrão
            Assert.Null(p0);
            var custom = FanProfile.Default with { CpuEntryTemperature = 70 };
            store.Save(custom);
            store.Save(custom with { CpuEntryTemperature = 72 });
            Assert.Equal(72, store.Load(out var p1).CpuEntryTemperature);
            Assert.Null(p1);
            Assert.True(File.Exists(path + ".bak"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Arquivo_invalido_usa_o_padrao_e_explica()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ks-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        try
        {
            File.WriteAllText(path, "{\"cpuCurve\":[{\"temperature\":50,\"percent\":90},{\"temperature\":60,\"percent\":10}],\"gpuCurve\":[],\"cpuEntryTemperature\":60,\"gpuEntryTemperature\":55,\"hysteresisCelsius\":3,\"rampUpPercentPerSecond\":15,\"rampDownPercentPerSecond\":3}");
            var p = new ProfileStore(path).Load(out var problem);
            Assert.Equal(FanProfile.Default, p);
            Assert.Contains("rejeitado", problem, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class ControlStateTests
{
    [Fact]
    public void Desativar_poe_auto_e_para_de_chamar_o_firmware_ate_reativar()
    {
        var fw = new SimulatedFirmware();
        fw.SetSensor(Sensor.CpuTemperature, 75);
        var e = new FanControlEngine(fw, FanProfile.Default);
        Assert.Equal(FanMode.Custom, e.Tick(1).Cpu.Mode);
        e.Disable("teste");
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Cpu));
        var calls = fw.Calls.Count;
        for (var i = 0; i < 120; i++)
        {
            Assert.Equal(EngineState.Disabled, e.Tick(1).State); // não retoma sozinho, nem depois de 30 s
        }

        Assert.Equal(calls, fw.Calls.Count); // desativado: nenhuma chamada ao firmware
        Assert.True(e.IsDisabled);
        e.Resume();
        Assert.Equal(EngineState.Curve, e.Tick(1).State);
    }

    [Fact]
    public void Marcador_persiste_a_escolha()
    {
        var path = Path.Combine(Path.GetTempPath(), "ks-test-" + Guid.NewGuid().ToString("N"), "control-disabled");
        try
        {
            var s = new ControlStateStore(path);
            Assert.False(s.IsDisabled);
            s.SetDisabled(true);
            Assert.True(new ControlStateStore(path).IsDisabled);
            s.SetDisabled(false);
            Assert.False(s.IsDisabled);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}

public class EngineProfileTests
{
    [Fact]
    public void Trocar_perfil_aplica_no_proximo_ciclo()
    {
        var fw = new SimulatedFirmware();
        fw.SetSensor(Sensor.CpuTemperature, 65);
        var e = new FanControlEngine(fw, FanProfile.Default);
        Assert.Equal(FanMode.Custom, e.Tick(1).Cpu.Mode);              // 65 °C ≥ entrada 60
        e.SetProfile(FanProfile.Default with { CpuEntryTemperature = 75 });
        Assert.Equal(FanMode.Auto, e.Tick(1).Cpu.Mode);                // agora abaixo da entrada
        Assert.Equal(FanMode.Auto, fw.GetFanMode(Fan.Cpu));
        Assert.Equal(75, e.Profile.CpuEntryTemperature);
    }
}
