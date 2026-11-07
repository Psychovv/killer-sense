using KillerSense.Core;
using KillerSense.Firmware;
using KillerSense.Ipc;

namespace KillerSense.Tests;

public class LightingProtocolTests
{
    private static readonly RgbColor Cyan = KeyboardLighting.AcerCyan;
    private static readonly RgbColor Red = new(0xFF, 0, 0);

    private static KeyboardLighting Effect(KeyboardEffect e, int speed = 7, LightDirection dir = LightDirection.Left) =>
        KeyboardLighting.Default with { Effect = e, Brightness = 60, Speed = speed, Direction = dir, Color = Red };

    [Fact]
    public void Zonas_acesas_vao_no_byte_5_com_subcomando_8()
    {
        var input = AcerProtocol.EncodeSetKeyboardZones(0x0B);
        Assert.Equal(16, input.Length);
        Assert.Equal(new byte[] { 8, 0, 0, 0, 0, 0x0B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, input);
        Assert.True(AcerProtocol.IsAllowedSetLed(input));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcerProtocol.EncodeSetKeyboardZones(0x10));
    }

    [Fact]
    public void Estatico_manda_efeito_0_velocidade_0_KLES_3_e_alvo_teclado()
    {
        var input = AcerProtocol.EncodeSetKeyboardBacklight(KeyboardLighting.Default with { Brightness = 40 });
        Assert.Equal(new byte[] { 0, 0, 40, 0, 1, 0, 0, 0, 3, 1, 0, 0, 0, 0, 0, 0 }, input);
        Assert.True(AcerProtocol.IsAllowedSetBacklight(input));
    }

    [Fact]
    public void Respiracao_leva_cor_e_velocidade_mas_nao_direcao()
    {
        var input = AcerProtocol.EncodeSetKeyboardBacklight(Effect(KeyboardEffect.Breathing));
        Assert.Equal(new byte[] { 1, 7, 60, 0, 1, 0xFF, 0, 0, 3, 1 }, input[..10]);
    }

    [Fact]
    public void Onda_leva_direcao_e_nao_leva_cor()
    {
        var input = AcerProtocol.EncodeSetKeyboardBacklight(Effect(KeyboardEffect.Wave, speed: 9));
        Assert.Equal(new byte[] { 3, 9, 60, 0, 2, 0, 0, 0, 3, 1 }, input[..10]);
    }

    [Theory]
    [InlineData(KeyboardEffect.Static)]
    [InlineData(KeyboardEffect.Breathing)]
    [InlineData(KeyboardEffect.Neon)]
    [InlineData(KeyboardEffect.Wave)]
    [InlineData(KeyboardEffect.Shifting)]
    [InlineData(KeyboardEffect.Zoom)]
    [InlineData(KeyboardEffect.Meteor)]
    [InlineData(KeyboardEffect.Twinkling)]
    public void Nenhum_efeito_manda_0xFF_nem_sai_do_permitido(KeyboardEffect effect)
    {
        var lighting = Effect(effect) with { Color = new RgbColor(0xFE, 0xFE, 0xFE) };
        var input = AcerProtocol.EncodeSetKeyboardBacklight(lighting);
        Assert.DoesNotContain(input[..5], b => b == 0xFF); // 0xFF = "não mudar" no ACPI
        Assert.Equal((byte)effect, input[0]);
        Assert.True(AcerProtocol.IsAllowedSetBacklight(input));
    }

    [Fact]
    public void Configuracao_fora_da_faixa_nao_vira_entrada()
    {
        Assert.Throws<ArgumentException>(() => AcerProtocol.EncodeSetKeyboardBacklight(KeyboardLighting.Default with { Brightness = 101 }));
        Assert.Throws<ArgumentException>(() => AcerProtocol.EncodeSetKeyboardBacklight(KeyboardLighting.Default with { Speed = 0 }));
        Assert.Throws<ArgumentException>(() => AcerProtocol.EncodeSetKeyboardBacklight(KeyboardLighting.Default with { Effect = (KeyboardEffect)8 }));
    }

    [Fact]
    public void Cor_da_zona_segue_o_formato_do_ACPI()
    {
        Assert.Equal(0xC7AE0001ul, AcerProtocol.EncodeSetZoneColor(0, Cyan));
        Assert.Equal(0x000000FF08ul, AcerProtocol.EncodeSetZoneColor(3, Red));
        Assert.Equal(Cyan, AcerProtocol.DecodeZoneColor(0x00000000C7AE0000)); // snapshot 01, zona 1
        Assert.Throws<FirmwareException>(() => AcerProtocol.DecodeZoneColor(0x01));
    }

    [Fact]
    public void Leitura_do_snapshot_vira_onda_direita_velocidade_5()
    {
        byte[] backlight = [0x03, 0x05, 0x64, 0xFF, 0x01, 0x00, 0x00, 0x00, 0x03, 0, 0, 0, 0, 0, 0];
        var zones = new ZoneColors(Cyan, RgbColor.Black, RgbColor.Black, RgbColor.Black);
        var l = AcerProtocol.DecodeKeyboardLighting(0, backlight, zones, 0x07);
        Assert.Equal(KeyboardEffect.Wave, l.Effect);
        Assert.Equal(100, l.Brightness);
        Assert.Equal(5, l.Speed);
        Assert.Equal(LightDirection.Right, l.Direction);
        Assert.Equal(zones, l.Zones);
        Assert.Equal(0x07, l.ZonesOn);
    }

    [Fact]
    public void Leitura_com_efeito_desconhecido_ou_status_ruim_falha()
    {
        byte[] backlight = [0x0B, 0x05, 0x64, 0xFF, 0x01, 0, 0, 0, 0x03, 0, 0, 0, 0, 0, 0];
        Assert.Throws<FirmwareException>(() => AcerProtocol.DecodeKeyboardLighting(0, backlight, default, 0));
        backlight[0] = 0;
        Assert.Throws<FirmwareException>(() => AcerProtocol.DecodeKeyboardLighting(1, backlight, default, 0));
    }

    [Fact]
    public void Leitura_do_estatico_com_velocidade_0_vira_o_padrao()
    {
        byte[] backlight = [0, 0, 0x32, 0xFF, 0, 0, 0, 0, 0x03, 0, 0, 0, 0, 0, 0];
        var l = AcerProtocol.DecodeKeyboardLighting(0, backlight, default, 0x0F);
        Assert.Equal(KeyboardLighting.DefaultSpeed, l.Speed);
        Assert.Equal(LightDirection.Right, l.Direction);
        Assert.Null(l.Validate());
    }

    [Fact]
    public void Zonas_acesas_saem_do_byte_4_do_GetGamingLED()
    {
        byte[] output = [0, 0, 0, 0, 0x0F, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]; // snapshot 03
        Assert.Equal(0x0F, AcerProtocol.DecodeKeyboardZones(0, output));
        Assert.Throws<FirmwareException>(() => AcerProtocol.DecodeKeyboardZones(2, output));
    }

    [Fact]
    public void So_as_zonas_do_teclado_passam_no_SetGamingLED()
    {
        var turbo = new byte[16];
        turbo[0] = 1; // TKST (Turbo)
        turbo[2] = 0x10;
        Assert.False(AcerProtocol.IsAllowedSetLed(turbo));
        var lightbar = new byte[16];
        lightbar[0] = 0x10;
        Assert.False(AcerProtocol.IsAllowedSetLed(lightbar));
        var extra = AcerProtocol.EncodeSetKeyboardZones(0x0F);
        extra[7] = 1;
        Assert.False(AcerProtocol.IsAllowedSetLed(extra));
        Assert.False(AcerProtocol.IsAllowedSetLed(new byte[8]));
    }

    [Fact]
    public void So_o_teclado_passa_no_SetGamingKBBacklight()
    {
        var ok = AcerProtocol.EncodeSetKeyboardBacklight(KeyboardLighting.Default);
        var lightbar = (byte[])ok.Clone();
        lightbar[9] = 2;
        Assert.False(AcerProtocol.IsAllowedSetBacklight(lightbar));
        var both = (byte[])ok.Clone();
        both[9] = 0;
        Assert.False(AcerProtocol.IsAllowedSetBacklight(both));
        var keep = (byte[])ok.Clone();
        keep[2] = 0xFF;
        Assert.False(AcerProtocol.IsAllowedSetBacklight(keep));
        var noDirection = (byte[])ok.Clone();
        noDirection[3] = 8;
        Assert.False(AcerProtocol.IsAllowedSetBacklight(noDirection));
    }

    [Fact]
    public void WMI_recusa_escritas_fora_do_teclado_antes_de_chamar_o_firmware()
    {
        using var fw = new AcerWmiFirmware();
        var turbo = new byte[16];
        turbo[0] = 1;
        var lightbar = AcerProtocol.EncodeSetKeyboardBacklight(KeyboardLighting.Default);
        lightbar[9] = 2;
        Assert.Throws<InvalidOperationException>(() => fw.Invoke("SetGamingRgbKb", 0x03));
        Assert.Throws<InvalidOperationException>(() => fw.Invoke("SetGamingLED", 0x08));
        Assert.Throws<InvalidOperationException>(() => fw.InvokeWithBytes("SetGamingLED", turbo));
        Assert.Throws<InvalidOperationException>(() => fw.InvokeWithBytes("SetGamingKBBacklight", lightbar));
        Assert.Throws<InvalidOperationException>(() => fw.InvokeWithBytes("SetGamingFanBehavior", new byte[16]));
        Assert.Throws<InvalidOperationException>(() => fw.InvokeWithBytes("SetGamingLEDBehavior", new byte[16]));
        Assert.Throws<InvalidOperationException>(() => fw.InvokeForBytes("SetGamingLED", 0x08));
    }

    [Fact]
    public void So_uma_zona_do_teclado_passa_no_SetGamingRgbKb()
    {
        Assert.True(AcerProtocol.IsAllowedSetZoneColor(AcerProtocol.EncodeSetZoneColor(2, Red)));
        Assert.False(AcerProtocol.IsAllowedSetZoneColor(0x0000FF00)); // máscara 0 = light bar
        Assert.False(AcerProtocol.IsAllowedSetZoneColor(0x03));
        Assert.False(AcerProtocol.IsAllowedSetZoneColor(0x1_0000_0001));
    }
}

public class KeyboardLightingTests
{
    [Theory]
    [InlineData("#00AEC7", 0x00, 0xAE, 0xC7)]
    [InlineData("ff8000", 0xFF, 0x80, 0x00)]
    [InlineData(" #FFFFFF ", 0xFF, 0xFF, 0xFF)]
    public void Cor_aceita_hex(string text, byte r, byte g, byte b)
    {
        Assert.True(RgbColor.TryParse(text, out var c));
        Assert.Equal(new RgbColor(r, g, b), c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("#GG0000")]
    [InlineData("#00AEC7FF")]
    public void Cor_recusa_o_resto(string? text) => Assert.False(RgbColor.TryParse(text, out _));

    [Fact]
    public void Padrao_e_valido_e_estatico_com_as_4_zonas()
    {
        Assert.Null(KeyboardLighting.Default.Validate());
        Assert.Equal(KeyboardLighting.AllZones, KeyboardLighting.Default.ZoneMask);
        Assert.False(LightingSettings.Default.Enabled);
    }

    [Fact]
    public void Validacao_recusa_faixas_erradas()
    {
        var d = KeyboardLighting.Default;
        Assert.NotNull((d with { Brightness = -1 }).Validate());
        Assert.NotNull((d with { Speed = 10 }).Validate());
        Assert.NotNull((d with { Direction = (LightDirection)3 }).Validate());
        Assert.NotNull((d with { ZonesOn = 0x10 }).Validate());
    }

    [Fact]
    public void Nos_animados_todas_as_zonas_acendem()
    {
        var l = KeyboardLighting.Default with { Effect = KeyboardEffect.Neon, ZonesOn = 0x01 };
        Assert.Equal(KeyboardLighting.AllZones, l.ZoneMask);
        Assert.True(l.IsZoneOn(3));
        Assert.False((l with { Effect = KeyboardEffect.Static }).IsZoneOn(3));
    }

    [Fact]
    public void Comparacao_ignora_o_que_o_efeito_nao_usa()
    {
        var a = KeyboardLighting.Default with { Effect = KeyboardEffect.Neon, Speed = 3 };
        Assert.True(a.SameVisibleAs(a with { Color = new RgbColor(1, 2, 3), Direction = LightDirection.Left, Zones = default }));
        Assert.False(a.SameVisibleAs(a with { Speed = 4 }));

        var s = KeyboardLighting.Default with { ZonesOn = 0x07 };
        Assert.True(s.SameVisibleAs(s with { Speed = 9, Zones = s.Zones.With(3, RgbColor.Black) })); // zona 4 apagada
        Assert.False(s.SameVisibleAs(s with { ZonesOn = 0x0F }));
        Assert.False(s.SameVisibleAs(s with { Zones = s.Zones.With(0, RgbColor.Black) }));
    }

    [Fact]
    public void Dto_ida_e_volta_mantem_tudo()
    {
        var l = new KeyboardLighting(KeyboardEffect.Shifting, 30, 2, LightDirection.Left, new RgbColor(1, 2, 3),
            new ZoneColors(new(10, 20, 30), new(40, 50, 60), new(70, 80, 90), new(100, 110, 120)), 0x05);
        var json = IpcProtocol.SerializeLighting(LightingSettingsDto.From(new LightingSettings(true, l)));
        var back = IpcProtocol.ParseLighting(json)!.TryToSettings(out var error);
        Assert.Null(error);
        Assert.Equal(new LightingSettings(true, l), back);
        Assert.Contains("\"Shifting\"", json, StringComparison.Ordinal);
        Assert.Contains("#0A141E", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Dto_invalido_explica_o_motivo()
    {
        var ok = KeyboardLightingDto.From(KeyboardLighting.Default);
        Assert.Null((ok with { Color = "azul" }).TryToLighting(out var e1));
        Assert.Contains("Cor", e1!, StringComparison.Ordinal);
        Assert.Null((ok with { Zones = ["#000000"] }).TryToLighting(out var e2));
        Assert.Contains("4 zonas", e2!, StringComparison.Ordinal);
        Assert.Null((ok with { Brightness = 200 }).TryToLighting(out var e3));
        Assert.Contains("Brilho", e3!, StringComparison.Ordinal);
        Assert.Null(new LightingSettingsDto(true, null!).TryToSettings(out _));
    }

    [Fact]
    public void Dto_sem_zonas_acesas_liga_todas()
    {
        var dto = KeyboardLightingDto.From(KeyboardLighting.Default with { ZonesOn = 0x01 }) with { ZonesOn = null };
        Assert.Equal(KeyboardLighting.AllZones, dto.TryToLighting(out _)!.ZonesOn);
    }
}

public class LightingControllerTests
{
    private static readonly KeyboardLighting Red = KeyboardLighting.Default with { Zones = ZoneColors.All(new RgbColor(0xFF, 0, 0)), Brightness = 70 };

    [Fact]
    public void Desligado_nao_toca_no_firmware()
    {
        var fw = new SimulatedFirmware();
        var ctl = new LightingController(fw, LightingSettings.Default);
        ctl.SetSettings(new LightingSettings(false, Red), allowed: true);
        Assert.False(ctl.ApplyIfEnabled(allowed: true));
        Assert.Null(ctl.ReadCurrent(allowed: true));
        Assert.Empty(fw.Calls);
        Assert.Equal(Red, ctl.Settings.Lighting); // guardou mesmo assim
    }

    [Fact]
    public void Ligado_grava_e_confere_lendo_de_volta()
    {
        var fw = new SimulatedFirmware();
        var ctl = new LightingController(fw, LightingSettings.Default);
        ctl.SetSettings(new LightingSettings(true, Red), allowed: true);
        Assert.Equal(["SetKeyboardLighting(00004600010000000301)", "GetKeyboardLighting"], fw.Calls);
        Assert.True(Red.SameVisibleAs(fw.Lighting));
        Assert.Equal(Red.Zones, ctl.ReadCurrent(allowed: true)!.Zones);
    }

    [Fact]
    public void Sem_permissao_so_guarda()
    {
        var fw = new SimulatedFirmware();
        var ctl = new LightingController(fw, LightingSettings.Default);
        ctl.SetSettings(new LightingSettings(true, Red), allowed: false);
        Assert.Null(ctl.ReadCurrent(allowed: false));
        Assert.Empty(fw.Calls);
        Assert.True(ctl.ApplyIfEnabled(allowed: true)); // ao reativar
        Assert.True(Red.SameVisibleAs(fw.Lighting));
    }

    [Fact]
    public void Teclado_que_nao_muda_vira_erro()
    {
        var fw = new SimulatedFirmware { IgnoreLightingWrites = true };
        var ctl = new LightingController(fw, LightingSettings.Default);
        var ex = Assert.Throws<FirmwareException>(() => ctl.SetSettings(new LightingSettings(true, Red), allowed: true));
        Assert.Contains("não confirmou", ex.Message, StringComparison.Ordinal);
        Assert.Equal(Red, ctl.Settings.Lighting);
    }

    [Fact]
    public void Falha_do_firmware_sobe()
    {
        var fw = new SimulatedFirmware { FailNextCall = "SetKeyboardLighting" };
        var ctl = new LightingController(fw, LightingSettings.Default);
        Assert.Throws<FirmwareException>(() => ctl.SetSettings(new LightingSettings(true, Red), allowed: true));
    }

    [Fact]
    public void Zona_apagada_fica_preta_no_simulado()
    {
        var fw = new SimulatedFirmware();
        var ctl = new LightingController(fw, LightingSettings.Default);
        ctl.SetSettings(new LightingSettings(true, Red with { ZonesOn = 0x09 }), allowed: true);
        Assert.Equal(0x09, fw.Lighting.ZonesOn);
        Assert.Equal(RgbColor.Black, fw.Lighting.Zones.Zone2);
        Assert.Equal(new RgbColor(0xFF, 0, 0), fw.Lighting.Zones.Zone4);
    }

    [Fact]
    public void Configuracao_invalida_e_recusada_sem_mudar_nada()
    {
        var fw = new SimulatedFirmware();
        var ctl = new LightingController(fw, LightingSettings.Default);
        Assert.Throws<ArgumentException>(() => ctl.SetSettings(new LightingSettings(true, Red with { Speed = 0 }), allowed: true));
        Assert.Equal(LightingSettings.Default, ctl.Settings);
        Assert.Empty(fw.Calls);
    }

    [Fact]
    public void Store_sem_arquivo_e_desligado_e_grava_atomico()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ks-light-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "lighting.json");
            var store = new KillerSense.Service.LightingStore(path);
            Assert.Equal(LightingSettings.Default, store.Load(out var p0));
            Assert.Null(p0);

            var s = new LightingSettings(true, Red);
            store.Save(s);
            store.Save(s with { Enabled = false });
            Assert.Equal(s with { Enabled = false }, store.Load(out var p1));
            Assert.Null(p1);
            Assert.True(File.Exists(path + ".bak"));

            File.WriteAllText(path, "{\"enabled\":true,\"lighting\":{\"effect\":\"Static\",\"brightness\":500}}");
            Assert.Equal(LightingSettings.Default, store.Load(out var p2));
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
