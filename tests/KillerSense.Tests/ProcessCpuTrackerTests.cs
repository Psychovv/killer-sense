using KillerSense.Core;

namespace KillerSense.Tests;

public class ProcessCpuTrackerTests
{
    private const long Second = 10_000_000; // em unidades de 100 ns

    [Fact]
    public void Primeira_leitura_so_serve_de_base()
    {
        var t = new ProcessCpuTracker();
        Assert.Empty(t.Update([new(10, "jogo.exe", 1, 5 * Second, 100)], 2 * Second, 4, 3));
    }

    [Fact]
    public void Porcentagem_e_de_todos_os_nucleos_e_soma_processos_com_o_mesmo_nome()
    {
        var t = new ProcessCpuTracker();
        t.Update([new(10, "jogo.exe", 1, 0, 0), new(20, "chrome.exe", 1, 0, 0), new(21, "chrome.exe", 1, 0, 0), new(30, "bloco.exe", 1, 0, 0)], 0, 4, 3);

        // 2 s com 4 núcleos = 8 s de capacidade. Jogo usou 4 s (50%), as duas abas do chrome 1 s cada (25%).
        var top = t.Update(
            [new(10, "jogo.exe", 1, 4 * Second, 500), new(20, "chrome.exe", 1, Second, 100), new(21, "Chrome.exe", 1, Second, 50), new(30, "bloco.exe", 1, 0, 10)],
            2 * Second, 4, 2);

        Assert.Equal(2, top.Count);
        Assert.Equal(new ProgramUsage("jogo", 1, 50, 500), top[0]);
        Assert.Equal("chrome", top[1].Name);
        Assert.Equal(2, top[1].Processes);
        Assert.Equal(25, top[1].CpuPercent, 6);
        Assert.Equal(150, top[1].PrivateBytes);
    }

    [Fact]
    public void Processo_novo_ou_id_reaproveitado_comeca_do_zero()
    {
        var t = new ProcessCpuTracker();
        t.Update([new(10, "velho.exe", 1, 100 * Second, 0)], 0, 1, 5);
        var top = t.Update([new(10, "novo.exe", 2, 3 * Second, 0), new(11, "outro.exe", 2, 9 * Second, 0)], Second, 1, 5);
        Assert.All(top, u => Assert.Equal(0, u.CpuPercent));
    }

    [Fact]
    public void Nome_sem_exe()
    {
        Assert.Equal("msedge", ProcessCpuTracker.DisplayName("msedge.exe"));
        Assert.Equal("System", ProcessCpuTracker.DisplayName("System"));
        Assert.Equal("Memory Compression", ProcessCpuTracker.DisplayName("Memory Compression"));
        Assert.Equal(".exe", ProcessCpuTracker.DisplayName(".exe"));
    }
}
