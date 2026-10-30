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

### Pendentes da Fase 3.2
- Instalar como serviço do Windows (mexe no registro — pedir antes) e testar: parada normal, reinício automático pelo Windows após kill, suspensão/hibernação, desligamento.
- Leitura de temperatura da GPU (NVML) para a curva da GPU; até lá a fan da GPU fica no Auto do EC.
- Emergência (Max) só foi testada com o firmware simulado (precisa de CPU ≥ 95 °C real).
- Stress de CPU/GPU com a curva ativa.
