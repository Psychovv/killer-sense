using KillerSense.Core;
using KillerSense.Ipc;

namespace KillerSense.Tests;

public class UserProfileTests
{
    private static readonly FanProfileDto Curve = FanProfileDto.From(FanProfile.Default);
    private static readonly KeyboardLightingDto Light = KeyboardLightingDto.From(KeyboardLighting.Default with { Effect = KeyboardEffect.Neon });

    private static UserProfile Full(string name = "Jogo") => new(
        name,
        OperatingMode.Turbo,
        new ProfileFans(false, Curve),
        GpuOffsetSettingsDto.From(GpuOffsetSettings.Default with { Enabled = true }),
        false,
        Light);

    /// <summary>Serviço falso: grava os pedidos e responde ok, menos os tipos em <paramref name="fail"/>.</summary>
    private static (List<IpcRequest> Sent, Func<IpcRequest, Task<IpcMessage>> Send) Fake(bool lightingEnabled = false, params IpcRequestType[] fail)
    {
        var sent = new List<IpcRequest>();
        return (sent, r =>
        {
            sent.Add(r);
            if (fail.Contains(r.Type))
            {
                return Task.FromResult(IpcMessage.Ack(false, "falhou"));
            }

            return Task.FromResult(r.Type == IpcRequestType.GetLighting
                ? IpcMessage.ForLighting(LightingSettingsDto.From(LightingSettings.Default with { Enabled = lightingEnabled }), null)
                : IpcMessage.Ack(true));
        });
    }

    [Fact]
    public void Padrao_Acer_e_valido_e_nao_mexe_na_bateria_nem_na_luz()
    {
        var p = UserProfile.AcerDefault;
        Assert.Null(p.Validate());
        Assert.Equal(OperatingMode.Balanced, p.Mode);
        Assert.True(p.Fans!.Auto);
        Assert.True(p.GpuOffsets!.Enabled);
        Assert.Null(p.ChargeLimit);
        Assert.Null(p.Lighting);
    }

    [Fact]
    public void Validacao_pega_nome_curva_gpu_e_luz_invalidos()
    {
        Assert.NotNull((Full() with { Name = " " }).Validate());
        Assert.NotNull((Full() with { Name = new string('x', 41) }).Validate());
        Assert.NotNull((Full() with { Mode = (OperatingMode)2 }).Validate());
        Assert.NotNull((Full() with { Fans = new ProfileFans(false) }).Validate());
        Assert.NotNull((Full() with { GpuOffsets = new GpuOffsetSettingsDto(true, [new(OperatingMode.Turbo, 500, 0)]) }).Validate());
        Assert.NotNull((Full() with { Lighting = Light with { Speed = 0 } }).Validate());
        Assert.Null(new UserProfile("Só o nome").Validate());
    }

    [Fact]
    public void Conjunto_recusa_nome_repetido_e_o_nome_da_Acer()
    {
        Assert.Null(new UserProfileSet([Full("A"), Full("B")]).Validate());
        Assert.NotNull(new UserProfileSet([Full("A"), Full("a ")]).Validate());
        Assert.NotNull(new UserProfileSet([Full("padrão acer")]).Validate());
        Assert.NotNull(new UserProfileSet([.. Enumerable.Range(0, 51).Select(i => new UserProfile($"P{i}"))]).Validate());
    }

    [Fact]
    public void Arquivo_ida_e_volta_e_arquivo_ruim_vira_lista_vazia()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ks-prof-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "profiles.json");
            Assert.Empty(UserProfileFile.Load(path, out var p0).Profiles);
            Assert.Null(p0);

            var set = new UserProfileSet([Full("Jogo"), new UserProfile("Estudo", OperatingMode.Quiet, new ProfileFans(true), ChargeLimit: true)], "Jogo");
            UserProfileFile.Save(path, set);
            UserProfileFile.Save(path, set);
            var back = UserProfileFile.Load(path, out var p1);
            Assert.Null(p1);
            Assert.Equal("Jogo", back.LastApplied);
            Assert.Equal(2, back.Profiles.Count);
            Assert.Equal(set.Profiles[1], back.Profiles[1]);
            Assert.Equal(Curve.CpuCurve, back.Profiles[0].Fans!.Curve!.CpuCurve);
            Assert.True(File.Exists(path + ".bak"));

            File.WriteAllText(path, "{\"profiles\":[{\"name\":\"\"}]}");
            Assert.Empty(UserProfileFile.Load(path, out var p2).Profiles);
            Assert.NotNull(p2);
            Assert.Throws<ArgumentException>(() => UserProfileFile.Save(path, new UserProfileSet([Full("A"), Full("A")])));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Importacao_recusa_o_que_nao_e_perfil()
    {
        Assert.Null(UserProfileFile.Parse("não é json", out var e1));
        Assert.NotNull(e1);
        Assert.Null(UserProfileFile.Parse("{\"profiles\":null}", out var e2));
        Assert.NotNull(e2);
        Assert.Equal("Jogo", UserProfileFile.Parse("{\"profiles\":[{\"name\":\"  Jogo \"}]}", out _)!.Profiles[0].Name);
    }

    [Fact]
    public async Task Aplica_na_ordem_modo_fans_gpu_bateria_luz()
    {
        var (sent, send) = Fake();
        var results = await ProfileApplier.ApplyAsync(Full(), send, onAc: true);
        Assert.All(results, r => Assert.True(r.Ok));
        Assert.Equal(
            [IpcRequestType.SetOperatingMode, IpcRequestType.SetProfile, IpcRequestType.Resume, IpcRequestType.SetGpuOffsets, IpcRequestType.SetChargeLimit, IpcRequestType.GetLighting, IpcRequestType.SetLighting],
            sent.Select(r => r.Type));
        Assert.Equal(OperatingMode.Turbo, sent[0].Mode);
        Assert.False(sent[4].Enabled);
    }

    [Fact]
    public async Task Turbo_na_bateria_vira_Equilibrado()
    {
        var (sent, send) = Fake();
        var results = await ProfileApplier.ApplyAsync(new UserProfile("T", OperatingMode.Turbo), send, onAc: false);
        Assert.Equal(OperatingMode.Balanced, sent.Single().Mode);
        Assert.Contains("tomada", results[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fans_no_Auto_so_pausam()
    {
        var (sent, send) = Fake();
        await ProfileApplier.ApplyAsync(new UserProfile("A", Fans: new ProfileFans(true)), send, onAc: null);
        Assert.Equal(IpcRequestType.RestoreAuto, sent.Single().Type);
    }

    [Fact]
    public async Task Curva_recusada_nao_retoma_e_as_outras_partes_seguem()
    {
        var (sent, send) = Fake(false, IpcRequestType.SetProfile);
        var results = await ProfileApplier.ApplyAsync(Full(), send, onAc: true);
        Assert.DoesNotContain(sent, r => r.Type == IpcRequestType.Resume);
        Assert.Contains(sent, r => r.Type == IpcRequestType.SetLighting);
        Assert.False(results.Single(r => r.Part == "Fans").Ok);
        var text = ProfileApplier.Summarize("Jogo", results);
        Assert.Contains("aplicado em parte", text, StringComparison.Ordinal);
        Assert.Contains("Fans: falhou", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Luz_mantem_o_liga_desliga_do_controle()
    {
        var (sent, send) = Fake(lightingEnabled: true);
        var results = await ProfileApplier.ApplyAsync(new UserProfile("L", Lighting: Light), send, onAc: true);
        var set = sent.Single(r => r.Type == IpcRequestType.SetLighting).Lighting!;
        Assert.True(set.Enabled);
        Assert.Equal(KeyboardEffect.Neon, set.Lighting.Effect);
        Assert.Equal("gravada no teclado", results.Single().Message);

        var (sent2, send2) = Fake(lightingEnabled: false);
        var r2 = await ProfileApplier.ApplyAsync(new UserProfile("L", Lighting: Light), send2, onAc: true);
        Assert.False(sent2.Single(r => r.Type == IpcRequestType.SetLighting).Lighting!.Enabled);
        Assert.Contains("desligado", r2.Single().Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Perfil_invalido_nao_manda_nada()
    {
        var (sent, send) = Fake();
        var results = await ProfileApplier.ApplyAsync(new UserProfile(""), send, onAc: true);
        Assert.Empty(sent);
        Assert.False(results.Single().Ok);
    }

    [Fact]
    public async Task Servico_fora_do_ar_vira_falha_da_parte()
    {
        Task<IpcMessage> Down(IpcRequest r) => throw new InvalidOperationException("Sem conexão com o serviço killer-sense.");
        var results = await ProfileApplier.ApplyAsync(Full(), Down, onAc: true);
        Assert.All(results, r => Assert.False(r.Ok));
        Assert.Equal(5, results.Count);
    }
}
