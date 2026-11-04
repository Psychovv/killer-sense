using KillerSense.Core;
using KillerSense.Firmware;
using KillerSense.Ipc;

namespace KillerSense.Tests;

/// <summary>Limite de carga de 80% (teste A8: bit0 de uFunctionStatus).</summary>
public class ChargeLimitTests
{
    private static int Reads(SimulatedFirmware fw) => fw.Calls.Count(c => c == "GetBatteryHealth");

    [Fact]
    public void Le_no_primeiro_ciclo_e_depois_so_a_cada_60_s()
    {
        var fw = new SimulatedFirmware { ChargeLimitOn = true };
        var m = new ChargeLimitMonitor(fw);
        m.Tick(1, allowed: true);
        Assert.True(m.IsOn);
        for (var i = 0; i < 59; i++) // 59 s depois da leitura: ainda não
        {
            m.Tick(1, allowed: true);
        }

        Assert.Equal(1, Reads(fw));
        m.Tick(1, allowed: true);
        Assert.Equal(2, Reads(fw));
    }

    [Fact]
    public void Mudanca_feita_pelo_care_center_aparece_na_proxima_leitura()
    {
        var fw = new SimulatedFirmware();
        var m = new ChargeLimitMonitor(fw);
        m.Tick(1, allowed: true);
        Assert.False(m.IsOn);
        fw.ChargeLimitOn = true;
        m.Tick(ChargeLimitMonitor.RefreshSeconds, allowed: true);
        Assert.True(m.IsOn);
    }

    [Fact]
    public void Sem_permissao_nao_chama_o_firmware_e_le_assim_que_volta()
    {
        var fw = new SimulatedFirmware();
        var m = new ChargeLimitMonitor(fw);
        m.Tick(1, allowed: false);
        m.Tick(1, allowed: false);
        Assert.Empty(fw.Calls);
        m.Tick(1, allowed: true);
        Assert.Equal(1, Reads(fw));
    }

    [Fact]
    public void Set_grava_e_confirma_por_leitura()
    {
        var fw = new SimulatedFirmware();
        var m = new ChargeLimitMonitor(fw);
        m.Set(true);
        Assert.True(fw.ChargeLimitOn);
        Assert.True(m.IsOn);
        Assert.Equal(["SetBatteryChargeLimit(True)", "GetBatteryHealth"], fw.Calls);
    }

    [Fact]
    public void Falha_na_leitura_vira_estado_desconhecido()
    {
        var fw = new SimulatedFirmware { ChargeLimitOn = true, FailNextCall = "GetBatteryHealth" };
        var m = new ChargeLimitMonitor(fw);
        m.Refresh();
        Assert.Null(m.IsOn);
    }

    [Fact]
    public void Status_bit0_e_o_limite()
    {
        Assert.True(new BatteryHealth(0x03, [1, 0, 0xFF, 0, 0]).ChargeLimitOn);  // A8 com o limite ligado
        Assert.False(new BatteryHealth(0x03, [0, 0, 0xFF, 0, 0]).ChargeLimitOn); // A8 desligado
        Assert.False(new BatteryHealth(0x02, [1]).ChargeLimitSupported);
        Assert.Null(new BatteryHealth(0x03, []).ChargeLimitOn);
    }

    [Fact]
    public void Bateria_so_permite_os_dois_metodos_e_a_mascara_do_limite()
    {
        Assert.Equal(["GetBatteryHealthControlStatus", "SetBatteryHealthControl"], AcerWmiFirmware.BatteryAllowlist.Order());
        Assert.Equal("BatteryControl", AcerWmiFirmware.BatteryClass);
        Assert.Equal(0x01, AcerWmiFirmware.ChargeLimitMask);
    }

    [Fact]
    public void Pedido_do_limite_ida_e_volta_no_json()
    {
        var line = IpcProtocol.Serialize(new IpcRequest(IpcRequestType.SetChargeLimit, Enabled: false));
        Assert.Contains("\"enabled\":false", line, StringComparison.Ordinal);
        var back = IpcProtocol.ParseRequest(line);
        Assert.Equal(IpcRequestType.SetChargeLimit, back?.Type);
        Assert.False(back?.Enabled);
    }
}
