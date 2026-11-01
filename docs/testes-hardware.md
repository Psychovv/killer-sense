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
- Cliente rodando como **usuário comum (sem admin)** conectou em `\.pipekiller-sense`, assinou e recebeu o estado a cada 1 s: `state Curve, CPU 67 °C, GPU 52 °C, Balanced, fan CPU Custom 23% 3529 rpm, fan GPU Auto 2290 rpm`. ✅
- Pedido desconhecido (`FormatDisk`) ⇒ `ack ok=false "Pedido inválido."`. ✅

### Pendentes da Fase 3.2
- Desligamento (verificar no log, no próximo desligamento normal, a linha "Auto do EC aplicado (desligamento do Windows)").
- Leitura de temperatura da GPU (NVML) para a curva da GPU; até lá a fan da GPU fica no Auto do EC.
- Emergência (Max) só foi testada com o firmware simulado (precisa de CPU ≥ 95 °C real).
- Stress de CPU/GPU com a curva ativa.
