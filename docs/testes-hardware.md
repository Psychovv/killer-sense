# killer-sense — testes do programa no hardware (PHN16-72)

Testes do **nosso** programa no notebook real (o mapeamento da interface Acer fica em [acer-interface.md](acer-interface.md)).
Plano de testes: [arquitetura.md §14](arquitetura.md). Saídas brutas em `recon-out/` (fora do git).

## Fase 3.1
| Teste | Data | Resultado |
|---|---|---|
| A15 — escritas Custom e FanSpeed (`HwTests custom`) | 2026-11-05 | ✅ ver acer-interface.md |
| A17 — curva % → RPM | 2026-11-05 | ✅ ver acer-interface.md |

## Fase 3.2 — serviço + watchdog (rodando como console de administrador, ainda não instalados)
Script: `scripts/hwtest/kill-service-test.ps1`. Serviço com `--cpu-entry 40` para a curva assumir com a CPU fria. Modo Equilibrado, na tomada.

### T1 — matar o serviço à força com a fan em Custom (2026-11-06 00:01)
| Momento | Estado lido no firmware |
|---|---|
| início | CPU Auto, GPU Auto |
| serviço rodando 15 s | **CPU Custom 61%** (~5000 rpm), GPU Auto |
| serviço morto (`Stop-Process -Force`) | — |
| +0,9 s / +1,7 s | CPU ainda Custom |
| **+2,6 s** | **CPU Auto** (watchdog: "sem batimento há 3,3 s… Auto aplicado, confirmado: True") |
- ✅ O watchdog devolveu o Auto em **~2,6 s** após o kill (meta: ≤ 3 s depois do último batimento).

### T2 — outro programa muda as fans para Auto com o serviço em curva (2026-11-06 00:02)
| Momento | Estado |
|---|---|
| serviço rodando 10 s | CPU Custom 68%, GPU Auto |
| `HwTests auto` (simula o PredatorSense) | — |
| +1 s … +5 s | CPU Auto o tempo todo; serviço em `ExternalOverride` ("controle pausado") |
- ✅ O serviço detectou a mudança e **não brigou** (não regravou o Custom). O watchdog não agiu (o serviço já não considera manual ativo).

### Instalação como serviços do Windows (2026-11-06 00:05, aprovada pelo usuário)
`scripts/install/install-services.ps1`: publicação autocontida (~80 MB em disco cada) em `C:\Program Files\KillerSense\{Service,Watchdog}`, serviços `KillerSense` e `KillerSenseWatchdog` (LocalSystem, início automático, reinício 5 s após falha). Remoção: `scripts/install/uninstall-services.ps1`.
- Ao iniciar: Auto aplicado e, com a CPU a ~68–70 °C, a curva padrão assumiu (CPU Custom 20–25%, ~3400 rpm); GPU no Auto do EC.

### T3 — parada normal do serviço instalado (2026-11-06 00:06)
- `Stop-Service KillerSense` com CPU em Custom 33% → **CPU Auto em < 0,5 s** (o próprio serviço restaurou). ✅
- `Start-Service` → recomeçou do Auto e a curva assumiu de novo (Custom 15%). ✅

### T4 — processo do serviço instalado morto à força (2026-11-06 00:06)
| Momento | Estado |
|---|---|
| antes | CPU Custom 15% |
| kill (`Stop-Process -Force` no PID do serviço) | — |
| +3,2 s (log do watchdog) | "sem batimento há 3,2 s… Auto aplicado (confirmado: true)" |
| +4,2 s (leitura) | CPU Auto |
| +6,4 s | **Windows reiniciou o serviço** (novo PID) |
| +9 s | serviço novo: Auto → curva (Custom 20%) |
- ✅ Watchdog + recuperação do Windows funcionando. O tempo até o Auto (~3,2 s após o último batimento) é ≤ 3 s de limite + até 1 s de ciclo; pode baixar o limite para 2,5 s se quiser.

### T5 — retomada automática depois de mudança externa (2026-11-06 00:23, serviço instalado e atualizado)
Regra aprovada: pausa, espera 30 s e retoma do Auto; mais de 3 mudanças em 10 min ⇒ fica pausado.
| Momento (log do serviço) | Estado |
|---|---|
| 00:23:42 | Curve: CPU Custom 43% |
| 00:23:54 | `HwTests auto` ⇒ **ExternalOverride** ("retomada automática em 30 s") |
| 00:24:25 | **Curve de novo** (Custom 13%), 31 s depois, começando do Auto |
- ✅ Retomada automática funcionando. O limite de 3 mudanças em 10 min está coberto pelos testes unitários.

### Suspensão (espera moderna) — 2026-11-06 00:08–00:13
- O notebook usa espera moderna (S0), sem S3: o Windows não avisa os serviços. Por decisão do usuário a curva continua ativa com a tela desligada (docs/arquitetura.md §6.3).
- A suspensão que "bugou" foi investigada: saídas da espera por mouse/teclado, sem erros no Windows, serviço pausado sem chamar o firmware desde 00:08:55. Sem indício de causa no killer-sense.

### T6 — temperatura da GPU (NVML) sem acordar a GPU (2026-11-06 00:39–00:41)
- `HwTests gpu`: estado de energia lido pelo Windows (cfgmgr32, `DEVPKEY_Device_PowerData`) = **D0**; NVML = **50 °C** (`nvidia-smi`: 51 °C). ✅
- Serviço instalado e atualizado: log "CPU 75 °C, **GPU 53 °C** | fan GPU Auto" — abaixo da entrada da GPU (55 °C), a fan da GPU fica no Auto do EC. ✅
- Na tomada a GPU ficou sempre em D0 (30 s amostrando). A detecção de GPU desligada (D3) ainda precisa ser vista na bateria: rodar `HwTests gpu --samples 30` fora da tomada e conferir "desligada (não consultada)".

### T7 — canal da interface (pipe) com o serviço instalado (2026-11-06 00:48)
- Cliente rodando como **usuário comum (sem admin)** conectou em `\\.\pipe\killer-sense`, assinou e recebeu o estado a cada 1 s: `state Curve, CPU 67 °C, GPU 52 °C, Balanced, fan CPU Custom 23% 3529 rpm, fan GPU Auto 2290 rpm`. ✅
- Pedido desconhecido (`FormatDisk`) ⇒ `ack ok=false "Pedido inválido."`. ✅

## Fase 3.3 — interface

### T8 — bandeja e "Encerrar killer-sense" (2026-11-06, serviço instalado e atualizado, na bateria)
- Abrir o app ⇒ "Enable" no log do serviço na primeira conexão (08:14:52, 08:16, 08:17, 08:18). ✅
- Fechar no X ⇒ janela destruída, app segue vivo na bandeja. ✅ (conferido pelo usuário: ícone, dica, abrir pelo clique)
- Bandeja → "Encerrar killer-sense" (13:35:29) ⇒ log `Comando da interface executado: "Disable"` e `Estado "Disabled" … fans no Auto do EC, PredatorSense no controle`; arquivo `%ProgramData%\KillerSense\control-disabled` criado; processo do app encerrado. ✅
- No mesmo intervalo houve suspensão de 13:05 a 13:35: o serviço aplicou o Auto do EC na suspensão e na retomada. ✅
- Memória (build Release, desenho pelo Skia em software): **~100 MB** de working set com a janela aberta (40 MB privados) e **~31 MB** só na bandeja (18 MB privados). Com o desenho pela GPU eram ~195 MB nos dois casos, quase tudo do driver de vídeo da Intel (`igc1464.dll`, `igd10*`).

### T9 — app instalado pelo menu Iniciar + salvar a curva (2026-11-06 13:41, na bateria)
- Aberto pelo atalho do menu Iniciar (`C:\Program Files\KillerSense\App\KillerSense.exe`) ⇒ "Enable" (13:41:15), marcador `control-disabled` apagado, estado Auto ⇒ **Curve** 1 s depois (CPU 64 °C, Custom 10%). ✅
- Página Fans: dois "SetProfile" (13:41:46 e 13:41:54) aceitos; `config.json` gravado às 13:41:54. ✅ (usuário: "tá funcionando")

## Fase 4.1 — modos

### T10 — troca de modo pela página Modos (2026-11-06 13:58, na tomada, versão instalada)
- Antes, no serviço simulado: Turbo aceito e lido de volta, Eco recusado na tomada ("O modo Eco só existe na bateria."), pedido sem modo recusado; na janela, o cartão "atual" e o chip do topo acompanharam.
- No hardware: o usuário trocou pela página ⇒ log `Modo de operação trocado pela interface: "Performance"` (13:58:29); estado publicado `operatingMode: Performance`, curva ativa com piso de 15%. ✅
- Achado: o serviço simulado gravava na mesma pasta e no mesmo nome de log do serviço real (os dois acabaram no `service-20261106_001.log`). Corrigido: o simulado grava em `simulated-service-*.log`.

## Fase 4.2 — bateria

### T11 — página Bateria (2026-11-06, serviço simulado e depois a versão instalada)
- Pipe: `SetChargeLimit enabled:true` aceito e o estado passou a `chargeLimitOn: true`; pedido sem valor recusado ("Valor do limite ausente.").
- Janela: carga 100%, "Na tomada", saúde 97%, capacidade atual 87,7 Wh, de fábrica 90,6 Wh (lidas do driver da bateria do Windows, sem admin); ciclos "não informado" (o driver devolve 0).
- No hardware, versão instalada (19:46–19:48, na tomada): desligar pela página ⇒ log `Limite de carga trocado pela interface: false` e o Care Center mostrou desligado; ligar ⇒ `true` (19:48:25) e o Care Center acompanhou. ✅
- Achado e corrigido: logo depois da troca, o botão voltava por um instante ao estado antigo (a página relia o estado publicado, que só muda no ciclo seguinte), e o segundo clique do usuário mandou "desligar" de novo (19:46:05 e 19:46:06). Agora a página guarda o valor aceito até o serviço publicar (máx. 10 s). No simulado, 16 s depois de cada clique o botão continua no valor pedido.
- Achado na instalação: publicar sem `--self-contained` gera serviços que não sobem (o PC só tem .NET 6); publicar sempre com `-r win-x64 --self-contained true`.
- Pendente: ver a carga parar em 80% depois de descarregar abaixo disso com o limite ligado.

## Configurações

### T12 — página Configurações (2026-11-06, simulado e depois a versão instalada)
- Simulado: salvar grava `%APPDATA%\KillerSense\app.json` e cria o atalho `killer-sense.lnk` na pasta Inicializar com `--autostart --tray`; com °F o Painel mostra "Últimos 2 minutos (86–212 °F)" e a página Fans "Agora: 131 °F" (CPU a 55 °C). A cor de destaque muda na hora.
- `--autostart` com o controle desativado ("Encerrar"): o serviço continua `Disabled` e a janela não abre; aberto pelo usuário, o controle volta.
- Achado: na prévia as cinco amostras de cor saíam todas na cor do Windows (a classe `accent` colidia com o estilo de botão de destaque do Fluent). Renomeada para `swatch`.
- Instalado: o usuário trocou unidade e cor, ligou o início com o Windows e abriu a pasta de logs. ✅ Atalho final: `C:\Program Files\KillerSense\App\KillerSense.exe --autostart --tray`.
- Pendente: confirmar no próximo login que o app sobe só na bandeja.

### T13 — pontos extras na curva (2026-11-06, simulado e versão instalada)
- Simulado (pipe): perfil com 10 pontos na CPU aceito e lido de volta; com 11, recusado ("A curva precisa de 2 a 10 pontos."). O limite de 10 já era do serviço.
- Instalado: o usuário adicionou com duplo clique, arrastou e removeu com clique direito. ✅ Não salvou (o `config.json` continua com 5 pontos por curva).

### T14 — painel ao vivo na página Modos (2026-11-06, simulado e versão instalada)
- Simulado: com a GPU desligada o painel mostra "GPU dormindo"; com `--sim-gpu`, clock, memória, uso, potência "de 80 W", temperatura e P0. A CPU usa os contadores reais do Windows (ex.: 4675 MHz, 12%, 34 W) e o limite sustentado do modo (85 W no Equilibrado).
- `HwTests gpu` no hardware, sem admin: NVML responde clock (210 MHz), memória (405 MHz), uso, potência (3,7 W), limite aplicado pelo driver (**75 W**, o HWiNFO mostra 80 nominal / 140 máx.) e P8.
- Instalado (21:04): o serviço publica `gpu {coreClockMhz 330, memoryClockMhz 405, powerWatts 3,1, powerLimitWatts 75, performanceState 8}`; o usuário trocou de modo e o limite sustentado acompanhou. ✅
- Pendente: com o HWiNFO fechado (ele mesmo mantém a GPU acordada), confirmar que a GPU ainda dorme com o killer-sense rodando (as leituras extras só acontecem em D0, junto com a temperatura).

## Fase 4.3 — GPU

### T15 — offset da GPU por modo (2026-11-06, simulado; hardware pendente)

**`HwTests gpu-oc` no hardware, sem admin:**
- `GetPstates20` responde.
- O P0 é editável: núcleo de −1000 a +1000, memória de −1000 a +3000.
- Offset atual: 0/0.
- Nada foi gravado.

**Testes de unidade (21):**
- Com o padrão (desligado), não toca no driver.
- O Turbo aplica +100/+200 e confere lendo de volta.
- Não regrava a cada ciclo.
- Outro modo volta a 0.
- Não acorda a GPU desligada.
- Reaplica se o driver perdeu o offset.
- Não briga com offset de outro programa.
- Failsafe de 85/78 °C e de leitura inválida.
- Desativar e parar zeram.
- Espera 60 s depois de um erro do driver.
- Os limites cortam valores altos.
- O Eco nunca tem offset.

**Simulado (pipe):**
- Desligado, não toca no driver.
- Ligado no Turbo, vai a +100/+200.
- No Equilibrado, volta a 0.
- Núcleo 900 é recusado.
- `gpu-offsets.json` é gravado.

**Simulado (tela):**
- Carrega o estado salvo, mostra "No driver agora" e "Alvo no modo atual" e marca o modo atual.
- Desligar e salvar zera o que o serviço gravou.
- Bug achado e corrigido: o cliente descartava a resposta `gpuOffsets`, então a tela sempre abria desligada.

**Pendente, com o usuário na frente do PC e o HWiNFO aberto:**
1. Instalar (admin).
2. Na tela GPU, ligar com o padrão da Acer e pôr o Turbo.
3. Conferir a memória a 8201 MHz no HWiNFO e a linha "Offset da GPU: … → núcleo +100 MHz, memória +200 MHz" no log.
4. Voltar ao Equilibrado (memória 8001) e desligar a opção.

**Como reverter:**
- desligar a opção na tela;
- `sc stop KillerSense` (zera ao parar);
- reiniciar o PC.

### Pendentes da Fase 3.2
- Desligamento (verificar no log, no próximo desligamento normal, a linha "Auto do EC aplicado (desligamento do Windows)").
- Leitura de temperatura da GPU (NVML) para a curva da GPU; até lá a fan da GPU fica no Auto do EC.
- Emergência (Max) só foi testada com o firmware simulado (precisa de CPU ≥ 95 °C real).
- Stress de CPU/GPU com a curva ativa.
