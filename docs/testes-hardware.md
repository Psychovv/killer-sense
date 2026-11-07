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
- Achado na instalação: publicar sem `--self-contained` gera serviços que não sobem (o PC só tem .NET 6); publicar sempre com `-r win-x64 --self-contained true` (desde 2026-11-07: `scripts/install/publish.ps1`, que já faz isso).
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

### T18 — Painel com uso, RAM e programas (2026-11-07, simulado com `--sim-gpu`)
- O Painel ganhou, na CPU, uso, clock e potência (os mesmos contadores do Windows da página Modos) e, na GPU, uso, clock e potência (o serviço já mandava). Também ganhou um cartão de Memória (RAM em uso) e "Programas que mais pesam" (os 3 com mais CPU, somando os processos de mesmo nome). O gráfico troca entre Temperatura e Uso.
- Nenhuma chamada nova ao firmware nem ao serviço. CPU, RAM e programas só são lidos com o Painel visível: CPU a cada 1 s, RAM e programas a cada 2 s.
- Programas: uma chamada `NtQuerySystemInformation` traz todos os processos com o tempo de CPU, sem abrir nenhum (sem admin, enxerga até os protegidos). Medido: ~3 ms de CPU por leitura com 349 processos, ou seja, ~1,5 ms/s com o Painel aberto.
- Visto no simulado: CPU 12%, 4.679 MHz, 40 W; GPU simulada 12%, 1.980 MHz, 18,5 W de 80 W; RAM 14,8 de 15,7 GB (94%); programas chrome (19), System, Spotify (8). O gráfico de uso mostrou a CPU (só com o Painel aberto) e a GPU.
- Achado: o notebook estava com a RAM em 94–97% durante o teste.
- Pendente: ver na versão instalada (o app precisa ser reinstalado).

## Fase 4.3 — GPU

### T15 — offset da GPU por modo (2026-11-06, simulado e versão instalada)

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

**Hardware (23:09–23:10, versão instalada, na tomada, usuário com o HWiNFO aberto):**
- Instalação aprovada (UAC): serviços parados, atualizados e de volta em Running. A opção veio desligada e o offset ficou intocado.
- Pelo pipe: a opção foi ligada com o padrão da Acer, o modo foi para o Turbo e ficou 20 s, depois voltou ao Equilibrado e a opção foi desligada.
- **CONFIRMADO:**
  - `SetPstates20` pelo serviço (SYSTEM) funciona: 0,7 s depois do Turbo, o driver lido de volta tinha **+100/+200**, mantido nos 20 s.
  - No Equilibrado, voltou a **0/0** em 0,4 s.
  - Sem erro do driver e sem o failsafe disparar (GPU a 47–49 °C).
  - O log mostra "Offset da GPU: núcleo 0 MHz, memória 0 MHz → núcleo +100 MHz, memória +200 MHz (modo Turbo)" e a volta.
- A GPU ficou em P8/P5 o tempo todo (repouso), então o clock de memória de 8201 MHz não apareceu (ele só vale no P0). Neste teste, o Turbo pelo killer-sense **não** levou a GPU a P0 em repouso.


**Em carga (23:16–23:18, jogo aberto, GPU em P0 com ~40% de uso e ~20 W, 58–59 °C):** Equilibrado por 15 s, Turbo por 30 s, Equilibrado por 15 s, com a opção ligada e desligada no fim.

| Fase | Offset no driver | Memória (NVML) | Memória (HWiNFO) |
|---|---|---|---|
| Equilibrado | 0/0 | 8001 MHz | 2000,2 MHz |
| Turbo | +100/+200, 1 s depois da troca | **8201 MHz**, 1 s depois | **2050,2 MHz**, ~3 s depois (intervalo de leitura do HWiNFO) |
| Equilibrado | 0/0 em 1 s | 8001 MHz em 2 s | 2000,2 MHz |

- **CONFIRMADO:** o +200 na memória pelo killer-sense chega ao clock real. Vale o mesmo que o PredatorSense aplica no Turbo (A13: 8201).
- Núcleo: com carga leve, o clock segue a demanda (1440–2295 MHz nas duas fases), então o +100 não aparece. SUPOSIÇÃO: ele desloca a curva tensão/frequência e só aparece com a GPU no limite de boost; o driver confirmou o +100 na leitura.
- Sem travamento ou artefato relatado nos 30 s, failsafe não disparou, nenhum erro do driver.

**Como reverter:**
- desligar a opção na tela;
- `sc stop KillerSense` (zera ao parar);
- reiniciar o PC.

## Fase 4.4 — luz do teclado

### T16 — luz do teclado pela página Iluminação (2026-11-07, versão instalada, com o usuário olhando o teclado)

**Resultado: tudo certo, inclusive a volta do standby depois da correção.** Cada gravação foi aceita e a leitura de volta bateu com o pedido; o usuário confirmou no teclado:

| Hora | Pedido | No teclado |
|---|---|---|
| 09:57:25 | Estático, brilho 100, ciano nas 4 zonas (primeira gravação de luz) | ciano nas 4 zonas ✔ |
| 09:58:11 | Estático, zonas vermelho / verde / azul / amarelo | da esquerda para a direita, nessa ordem ✔ (zona 1 = esquerda) |
| 10:01:21 | Zona 2 apagada (`KBST = 0x0D`), brilho 30 | zona 2 apagada, as outras mais fracas ✔ |
| 10:02:01 | 4 zonas acesas, brilho 0 | teclado todo apagado ✔ |
| 10:03:17 | Respiração vermelha, velocidade 1 | aceito e lido de volta; o usuário não chegou a descrever (a tela apagou) |
| 10:58:58 | Onda, velocidade 9, para a esquerda (`KBED = 2`) | mais rápida que a Onda da Acer (5) e para a esquerda ✔ |

- Os números dos efeitos foram CONFIRMADOS também no perfil do próprio serviço de luz da Acer (`LightingProfile.ini`: STATIC 0 … TWINKLING 7), igual à nossa tabela. Por isso não testamos efeito por efeito.
- **Standby:** às 10:13 o notebook entrou no standby moderno; ao acordar (10:50:09), o `AcerLightingService` regravou a Onda dele (10:50:14) e o killer-sense não regravou a sua: o standby moderno não manda o aviso de suspensão ao serviço. Corrigido no mesmo dia (`LightingRecheck`): o serviço trata uma parada de mais de 5 s no laço como volta do standby e confere a luz aos 10 s e aos 40 s (também depois de retomar, do login, do desbloqueio e do início do serviço), regravando só se o teclado mostrar outra luz. Instalado às 11:03; o reinício do serviço regravou a luz salva. **CONFIRMADO no standby seguinte:** o notebook dormiu das 13:44:28 às 14:08:04 (eventos 506/507) e às 14:08:14 o log trouxe "Luz do teclado regravada (volta do standby): outro programa tinha trocado a luz". Durante o standby o Windows acordou uns 30 s a cada minuto para manutenção (o log ganhou uma linha "O serviço ficou parado 30 s" por minuto) e, logo depois de acordar, acertou o relógio, o que gerou uma segunda linha. A partir da versão seguinte o serviço registra só paradas de 2 min ou mais, uma vez; a detecção continua a mesma.
- Reiniciar o serviço (duas instalações): a luz salva foi regravada no início ✔.

Plano original do teste, para referência:

Feito em 2026-11-07 e testado só no simulado (testes de unidade + a página contra o serviço simulado). No notebook, o controle da luz vem **desligado**: sem ligar a opção, o serviço não faz nenhuma chamada de luz ao firmware.

O que o serviço manda, igual à DLL da Acer (detalhes em docs/acer-interface.md, "Teclado"):
1. `SetGamingLED [8, 0, 0, 0, 0, zonas]`: zonas acesas.
2. `SetGamingKBBacklight [efeito, velocidade, brilho, 0, direção, R, G, B, 3, 1]`: só o teclado.
3. No estático, `SetGamingRgbKb` em cada zona, com 30 ms entre elas. Zona apagada recebe preto.
4. Lê de volta (`GetGamingKBBacklight`, `GetGamingRgbKb`, `GetGamingLED`, só leituras do EC) e confere.

A lista de permissão só aceita essas três escritas com o alvo "teclado". `SetGamingLED` com `[0] = 1` (Turbo) e a light bar ficam bloqueados.

Roteiro (com o usuário olhando o teclado):
1. Instalar a versão nova (`update-services.ps1`, UAC).
2. Página Iluminação → "No teclado agora" deve aparecer como "não lido". Ligar "Controlar a luz" e salvar sem mudar nada. A primeira leitura deve bater com o PredatorSense.
3. Estático com as 4 zonas em cores diferentes. Conferir a ordem (zona 1 = esquerda).
4. Apagar uma zona; brilho 30; brilho 0 (deve apagar tudo).
5. Cada efeito, com velocidade 1 e 9. Conferir se o número do efeito bate com o nome (Respiração, Neon, Onda, Deslocamento, Zoom, Meteoro, Cintilante) e se a direção da Onda e do Deslocamento bate com a tela.
6. Reiniciar o serviço: ele deve regravar a luz salva ao iniciar.
7. Para reverter: desligar "Controlar a luz" e escolher a luz no PredatorSense.

O que era SUPOSIÇÃO antes do teste:
- direção 1 = direita e 2 = esquerda (enum do PredatorSense): CONFIRMADO;
- `KBST` acender e apagar zonas no estático: CONFIRMADO;
- o EC guardar a luz depois de reiniciar ou suspender: quem troca a luz ao acordar é o serviço de luz da Acer; o killer-sense agora regrava a dele (falta ver no log, ver acima). Depois de reiniciar o Windows: ainda não testado.

## Fase 4.5 — perfis

### T17 — perfis pela página Perfis e pela bandeja (PENDENTE: testar junto com o usuário)

Feito em 2026-11-07 e testado só no simulado: testes de unidade e a página contra o serviço simulado, no build de depuração e no publicado (com trimming). Novo (pega a configuração atual), trocar o modo, Salvar, Aplicar e voltar ao "Padrão Acer": tudo certo, e o `profiles.simulated.json` ficou correto. O submenu "Perfis" da bandeja compila, mas não foi clicado num teste automático.

Os perfis não têm chamada nova ao firmware: aplicar um perfil só manda os comandos que as outras páginas já usam.

Roteiro (versão instalada):
1. Perfis → Novo. Conferir se o perfil pegou o modo, a curva, o offset da GPU, a bateria e a luz que estão valendo.
2. Criar um "Silencioso + Auto" e um "Turbo + curva" (na tomada). Aplicar cada um e conferir no Painel, em Fans, em GPU e em Bateria.
3. Aplicar o "Turbo + curva" na bateria: deve ir para Equilibrado e avisar.
4. Trocar de perfil pelo menu da bandeja; a marca deve ir para o último aplicado.
5. Exportar, excluir um perfil, importar de volta.
6. Aplicar "Padrão Acer": Equilibrado, fans no Auto do EC, offset da GPU da Acer.

## Peso e eficiência (2026-11-07)

Comparação com o PredatorSense, medida com os dois rodando (o PredatorSense aberto em segundo plano desde o login):

| | killer-sense | PredatorSense 5.0.1341 (Electron) |
|---|---|---|
| RAM com a janela fechada (total / privada) | ~141 MB / ~81 MB (serviço 74/53, watchdog 36/10, bandeja ~31/18) | 407 MB / 510 MB, só os 4 processos do app |
| CPU média | serviço ~3,5 ms/s, watchdog 0,26 ms/s | app 2,8–4,7 ms/s |
| Disco | 365 MB, 702 arquivos | 491 MB, só o app |

Mudanças feitas a partir disso:
- **Uma leitura do modo das fans por ciclo**: `GetGamingFanBehavior(0x09)` devolve as duas fans (formato do A7). Cada leitura do modo é um SMI, que pausa todos os núcleos por um instante. Antes eram 2 por segundo com as duas fans em curva, mais 2 ao detectar mudança externa e 2 a cada `RestoreAuto`. Agora é 1 em cada caso, e nenhuma com as duas no Auto.
- **Publicação** (`scripts/install/publish.ps1`):
  - sem `.pdb`: os do SkiaSharp e do HarfBuzz somavam ~100 MB;
  - sem o ANGLE (`av_libglesv2.dll`): o desenho é sempre pela CPU;
  - sem o `packages.lock.json`;
  - app com trimming;
  - serviço e watchdog dividindo uma só cópia do .NET em `Services\`. O script confere que todo arquivo em comum é idêntico.
  - Resultado: **365 → 119 MB** (app 205 → 39 MB; serviços 160 → 80 MB).
  - O trimming do serviço e do watchdog foi descartado: a biblioteca de WMI (`Microsoft.Management.Infrastructure`) e o Serilog geram avisos de trimming, e o WMI é o caminho do failsafe.
- **GC sem a thread em segundo plano** no serviço e no watchdog (como já era no app). Medido no serviço simulado: −2 threads; a memória quase não muda (44,8 → 44,2 MB de working set).
- **A leitura da bateria (a cada 5 s) só roda com a página Bateria aberta**. Antes rodava também só na bandeja.

**App com trimming** (2026-11-07, serviço simulado, mesma automação de interface nos dois):

| | antes | depois |
|---|---|---|
| Janela aberta no Painel | 98,7 MB / 38,3 MB | 89,1 MB / 36,7 MB |
| Depois de passar pelas 9 páginas | 154,4 MB / 80,1 MB | 144,8 MB / 78,8 MB |
| Só na bandeja | 24,7 MB / 45,8 MB | 20,0 MB / 46,6 MB |
| Tempo até a janela aparecer | 0,64–0,75 s | 0,81–0,95 s |

- No tempo até a janela, a versão nova rodou da pasta do OneDrive e a antiga de Program Files.
- As 9 páginas abriram com dados ao vivo do simulado.
- A árvore de acessibilidade (UI Automation) ficou igual: 52 elementos e 10 botões.

**Instalado em 2026-11-07 (09:54, com UAC):** o `update-services.ps1` migrou de `Service\` e `Watchdog\` para `Services\` sem erro (parou, trocou o caminho dos dois serviços, apagou as pastas antigas e iniciou). RAM medida às 11:13, serviço rodando havia 10 min, killer-sense ativo, luz ligada:

| | antes (pastas separadas) | depois (`Services\` + GC sem thread) |
|---|---|---|
| Serviço (working set / privada) | 74 / 53 MB | 83,1 / 40,3 MB |
| Watchdog | 36 / 10 MB | 40,2 / 9,4 MB |

- A memória privada do serviço caiu 13 MB. O working set subiu um pouco, mas boa parte dele agora são as DLLs do .NET que os dois processos dividem.
- Observação, não conferida: na primeira abertura do app novo, ele levou ~22 s até falar com o serviço (antes a janela abria em menos de 1 s). Pode ser o antivírus olhando os arquivos recém-copiados; ver se repete.

**Pendente:**
- repetir o T3/T4 (parada normal e kill do serviço) com a nova pasta.

### Pendentes da Fase 3.2
- Desligamento (verificar no log, no próximo desligamento normal, a linha "Auto do EC aplicado (desligamento do Windows)").
- ~~Leitura de temperatura da GPU (NVML) para a curva da GPU~~: feito no T6. A curva da GPU usa o NVML; com a GPU desligada (Optimus), a fan dela fica no Auto do EC.
- Emergência (Max) só foi testada com o firmware simulado (precisa de CPU ≥ 95 °C real).
- Stress de CPU/GPU com a curva ativa.
