using KillerSense.Core;
using KillerSense.Firmware;

namespace KillerSense.Tests;

/// <summary>Valores esperados vêm das leituras reais no PHN16-72 (docs/acer-interface.md).</summary>
public class AcerProtocolTests
{
    private static Dictionary<Fan, FanMode> Both(FanMode m) => new() { [Fan.Cpu] = m, [Fan.Gpu] = m };

    [Theory]
    [InlineData(FanMode.Auto, 0x00410009UL)]   // escrita 01, testada no hardware
    [InlineData(FanMode.Max, 0x00820009UL)]    // código da Acer
    [InlineData(FanMode.Custom, 0x00C30009UL)] // código da Acer
    public void SetFanBehavior_cpu_e_gpu_bate_com_os_valores_mapeados(FanMode mode, ulong expected) =>
        Assert.Equal(expected, AcerProtocol.EncodeSetFanBehavior(Both(mode)));

    [Fact]
    public void SetFanBehavior_cpu_custom_e_gpu_auto() =>
        Assert.Equal(0x00430009UL, AcerProtocol.EncodeSetFanBehavior(new Dictionary<Fan, FanMode> { [Fan.Cpu] = FanMode.Custom, [Fan.Gpu] = FanMode.Auto }));

    [Fact]
    public void SetFanBehavior_rejeita_lista_vazia_e_modo_invalido()
    {
        Assert.Throws<ArgumentException>(() => AcerProtocol.EncodeSetFanBehavior(new Dictionary<Fan, FanMode>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerProtocol.EncodeSetFanBehavior(new Dictionary<Fan, FanMode> { [Fan.Cpu] = (FanMode)0 }));
    }

    [Theory]
    [InlineData(0x4100UL, FanMode.Auto)]   // smi-01 (Auto)
    [InlineData(0xC300UL, FanMode.Custom)] // smi-02 (Custom 40/30)
    [InlineData(0x8200UL, FanMode.Max)]    // smi-03 (Max)
    public void FanBehavior_decodifica_cpu_e_gpu(ulong output, FanMode expected)
    {
        Assert.Equal(expected, AcerProtocol.DecodeFanMode(output, Fan.Cpu));
        Assert.Equal(expected, AcerProtocol.DecodeFanMode(output, Fan.Gpu));
    }

    [Fact]
    public void FanBehavior_com_status_de_erro_lanca() =>
        Assert.Throws<FirmwareException>(() => AcerProtocol.DecodeFanMode(0x02, Fan.Cpu)); // máscara 0 → status 2

    [Theory]
    [InlineData(Fan.Cpu, 40, 0x2801UL)]
    [InlineData(Fan.Gpu, 30, 0x1E04UL)]
    [InlineData(Fan.Cpu, 0, 0x0001UL)]
    [InlineData(Fan.Gpu, 100, 0x6404UL)]
    public void SetFanSpeed_codifica_percentual_e_id(Fan fan, int pct, ulong expected) =>
        Assert.Equal(expected, AcerProtocol.EncodeSetFanSpeed(fan, pct));

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void SetFanSpeed_rejeita_fora_de_0_a_100(int pct) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerProtocol.EncodeSetFanSpeed(Fan.Cpu, pct));

    [Fact]
    public void FanSpeed_decodifica_byte1() => Assert.Equal(40, AcerProtocol.DecodeFanSpeed(0x0000000000062800)); // byte2 = lixo de buffer

    [Theory]
    [InlineData(Sensor.CpuTemperature, 0x0101u)]
    [InlineData(Sensor.CpuFanRpm, 0x0201u)]
    [InlineData(Sensor.GpuFanRpm, 0x0601u)]
    public void SysInfo_codifica_grupo_e_indice(Sensor s, uint expected) => Assert.Equal(expected, AcerProtocol.EncodeSysInfo(s));

    [Theory]
    [InlineData(0x3D00UL, 61)]    // CTML 61 °C
    [InlineData(0x083100UL, 2097)] // C1FR 2097 rpm
    [InlineData(0x1B4000UL, 6976)] // Max
    public void SysInfo_decodifica_16_bits(ulong output, int expected) => Assert.Equal(expected, AcerProtocol.DecodeSysInfo(output));

    [Theory]
    [InlineData(0x0000UL, OperatingMode.Quiet)]
    [InlineData(0x0100UL, OperatingMode.Balanced)]
    [InlineData(0x0400UL, OperatingMode.Performance)]
    [InlineData(0x0500UL, OperatingMode.Turbo)]
    [InlineData(0x0600UL, OperatingMode.Eco)]
    public void Modo_de_operacao_decodifica(ulong output, OperatingMode expected) =>
        Assert.Equal(expected, AcerProtocol.DecodeOperatingMode(output));

    [Fact]
    public void Modo_de_operacao_desconhecido_lanca() =>
        Assert.Throws<FirmwareException>(() => AcerProtocol.DecodeOperatingMode(0x0200));

    [Theory]
    [InlineData(OperatingMode.Quiet, 0x00BUL)]
    [InlineData(OperatingMode.Balanced, 0x10BUL)] // código da Acer
    [InlineData(OperatingMode.Performance, 0x40BUL)]
    [InlineData(OperatingMode.Turbo, 0x50BUL)]    // código da Acer
    [InlineData(OperatingMode.Eco, 0x60BUL)]
    public void SetOperatingMode_codifica_modo_no_byte_1(OperatingMode mode, ulong expected)
    {
        Assert.Equal(expected, AcerProtocol.EncodeSetOperatingMode(mode));
        Assert.True(AcerProtocol.IsAllowedMiscSetting(expected));
    }

    [Fact]
    public void SetOperatingMode_rejeita_modo_invalido() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerProtocol.EncodeSetOperatingMode((OperatingMode)2));

    [Theory]
    [InlineData(0x001UL)]    // TKST
    [InlineData(0x10FUL)]    // FMKY
    [InlineData(0x002UL)]    // SMI
    [InlineData(0x006UL)]    // SMI
    [InlineData(0x008UL)]    // SMI
    [InlineData(0x20BUL)]    // modo 2 não existe
    [InlineData(0x1010BUL)]  // bytes extras
    public void MiscSetting_fora_do_modo_e_bloqueado(ulong input) =>
        Assert.False(AcerProtocol.IsAllowedMiscSetting(input));

    [Fact]
    public void WMI_recusa_misc_setting_fora_do_modo_antes_de_chamar_o_firmware()
    {
        using var fw = new AcerWmiFirmware();
        var ex = Assert.Throws<InvalidOperationException>(() => fw.Invoke("SetGamingMiscSetting", 0x001));
        Assert.Contains("recusada", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, OperatingMode.Turbo, true)]
    [InlineData(true, OperatingMode.Eco, false)]
    [InlineData(false, OperatingMode.Turbo, false)]
    [InlineData(false, OperatingMode.Performance, false)]
    [InlineData(false, OperatingMode.Eco, true)]
    [InlineData(false, OperatingMode.Balanced, true)]
    public void Modos_permitidos_por_fonte_de_energia(bool onAc, OperatingMode mode, bool allowed) =>
        Assert.Equal(allowed, OperatingModes.IsAllowed(mode, onAc));

    [Fact]
    public void Allowlist_so_tem_os_metodos_esperados_e_nenhuma_classe_perigosa()
    {
        string[] expected =
        [
            "GetGamingFanBehavior", "SetGamingFanBehavior", "GetGamingFanSpeed", "SetGamingFanSpeed", "GetGamingSysInfo", "GetGamingMiscSetting", "SetGamingMiscSetting",
            "GetGamingLED", "SetGamingLED", "GetGamingRgbKb", "SetGamingRgbKb", "GetGamingKBBacklight", "SetGamingKBBacklight",
        ];
        Assert.Equal(expected.Order(), AcerWmiFirmware.Allowlist.Keys.Order());
        Assert.Equal("AcerGamingFunction", AcerWmiFirmware.GamingClass);
        string[] forbidden = ["BIOSSetting", "UtilityFunction", "DataTrans", "MethodForPowerShell", "AcerBiosConfigurationTool"];
        Assert.DoesNotContain(AcerWmiFirmware.GamingClass, forbidden);
    }
}
