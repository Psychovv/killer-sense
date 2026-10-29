# killer-sense — Arquitetura (Fase 2, RASCUNHO para aprovação)

Base: tudo o que está em [acer-interface.md](acer-interface.md). Onde algo ainda é SUPOSIÇÃO, este documento diz qual teste valida.

## 1. Decisões já tomadas pelo usuário (2026-11-05)
| # | Decisão |
|---|---|
| D1 | O software da Acer continua instalado. O nosso programa **não o desativa** e o trata como "outro escritor" que deve ser detectado, não combatido. |
| D2 | **Sem driver de baixo nível** por enquanto (potência da CPU, MSR etc.). Entra numa fase posterior. |
| D3 | Ordem: fans → modos → bateria → teclado/extras → GPU. Paridade com o PredatorSense primeiro; monitoramento extra depois. |

## 2. Linguagem e plataforma — **C# / .NET 10 (LTS) + Avalonia 12** (interface escolhida pelo usuário em 2026-11-05)
Motivos do C#/.NET:
- **WMI nativo** (`Microsoft.Management.Infrastructure`, o mesmo CIM que usamos no PowerShell em todos os testes).
- **Serviço do Windows** de primeira classe (`Microsoft.Extensions.Hosting.WindowsServices`): eventos de parada, desligamento (pre-shutdown), suspensão e retomada.
- **Interop simples** com as DLLs da NVIDIA (`nvml.dll`, e depois `nvapi64.dll`) e com HID (`hid.dll`) via `LibraryImport`.
- Um idioma só para serviço, interface e testes.

Motivos do Avalonia (comparado com WinUI 3 e WPF):
- **Visual próprio sem limite** (tema escuro "gamer", brilhos, mostradores, controles e animações personalizados), que é o objetivo do usuário (bonito, na linha do MSI Afterburner).
- **O mais leve** das opções avaliadas: renderização por GPU (Skia) e suporte a **Native AOT** (abre rápido, menos memória, sem depender do runtime instalado).
- Ícone de bandeja embutido (`TrayIcon`) e bibliotecas de gráfico prontas (LiveCharts2 ou ScottPlot para Avalonia).

Alternativas descartadas: WinUI 3 (mais "cara de Windows", um pouco mais pesado, empacotamento e bandeja mais trabalhosos); WPF (personalização profunda trabalhosa); Electron/Tauri (interface em navegador: é justamente o que deixa o PredatorSense pesado); C++/Rust/Python para o núcleo (mais risco ou mais trabalho num processo privilegiado, sem ganho real).

Ambiente: hoje só há o runtime .NET 6 instalado. Será preciso o **SDK do .NET 10** (download pede aprovação). Os pacotes NuGet (Avalonia, gráficos, CIM) também são downloads e serão listados para aprovação antes da Fase 3.

## 3. Visão geral

```
┌──────────────────────────────┐
│ KillerSense.Tray  (usuário)   │  sempre ativo, sem janela: ícone, menu rápido
│  Avalonia TrayIcon, Native AOT│  (modo, perfil, "voltar ao Auto"), abre a janela
└──────────────┬───────────────┘
               │ inicia sob demanda
┌──────────────▼───────────────┐        pipe nomeado (ACL: usuário + SYSTEM)
│ KillerSense.App  (usuário)    │ ◄──────────────────────────────────────────┐
│  janela Avalonia, Native AOT  │   pedidos JSON  /  telemetria (stream)      │
│  ENCERRA ao fechar a janela   │                                             │
└──────────────────────────────┘                                              │
  (a bandeja também fala com o serviço pelo mesmo pipe) ──────────────────────┤
                                                                              │
┌─────────────────────────────────────────────────────────────────────────┐   │
│ KillerSense.Service  (LocalSystem, serviço do Windows)                   │◄──┘
│  ├─ Guardião (failsafe, estado seguro, volta ao Auto)                    │
│  ├─ Controlador de fans (laço 1 Hz, curvas, histerese, rampa)            │
│  ├─ Telemetria (EC SysInfo, NVML, contadores do Windows)                 │
│  ├─ Perfis (modo térmico + curvas + extras), configuração persistida     │
│  └─ Firmware.Acer (ÚNICO ponto de contato com o WMI; lista de permissão) │
└─────────────────────────────────────────────────────────────────────────┘
        │ WMI root\WMI                │ NVML             │ HID (teclado)
        ▼                             ▼                  ▼
  AcerGamingFunction / BatteryControl   driver NVIDIA     teclado Acer
        │ ACPI WMBH/WMBE → EC / SMI

┌──────────────────────────────┐
│ KillerSense.Watchdog (SYSTEM) │  processo mínimo: se o serviço parar de dar sinal
│  (serviço separado)           │  com fan em manual, devolve o Auto (0x00410009)
└──────────────────────────────┘
```

Projetos da solução:
| Projeto | Papel |
|---|---|
| `KillerSense.Firmware` | biblioteca: wrappers tipados dos métodos WMI da Acer, **lista de permissão** fixa, validação de faixas, decodificação das respostas. Nenhum outro projeto fala com WMI. |
| `KillerSense.Core` | modelos (perfis, curvas, telemetria), contratos do pipe, validação de configuração. Sem acesso a hardware. |
| `KillerSense.Service` | serviço principal (laço de controle, telemetria, perfis, servidor do pipe). |
| `KillerSense.Watchdog` | serviço mínimo de segurança (ver §6.3). |
| `KillerSense.Tray` | ícone de bandeja (Avalonia, Native AOT, sem janela). Sem privilégio. Inicia com o Windows. |
| `KillerSense.App` | janela principal (Avalonia, Native AOT). Sem privilégio. Processo encerra ao fechar a janela. |
| `KillerSense.UI.Theme` | design system: cores, tipografia, estilos, controles próprios (curva, mostrador, gráfico). |
| `KillerSense.Tests` | testes unitários (curvas, histerese, failsafe, decodificação) com um firmware **simulado**. |
| `KillerSense.HwTests` | roteiros de teste no hardware real (manuais, com aprovação). |

## 4. Camada de firmware (`KillerSense.Firmware`)
- Expõe só operações de alto nível e tipadas, por exemplo `SetFanMode(FanMask, FanMode)`, `SetFanSpeed(FanId, Percent)`, `GetSysInfo(Sensor)`, `SetOperatingMode(OpMode)`.
- **Lista de permissão (allowlist)** no código: só `AcerGamingFunction` (métodos 1–23 mapeados), `BatteryControl` (19–23) e, se preciso, `AcerGenericMethod`. As classes ⛔ (`BIOSSetting`, `UtilityFunction`, `DataTrans`, `MethodForPowerShell`, `AcerBiosConfigurationTool`) não têm wrapper e uma chamada a elas lança exceção.
- Validação antes de enviar: % de fan 0–100 (o próprio ACPI recusa > 100), modos de fan só 1/2/3, `OPMS` só 0/1/4/5/6, máscaras só com bits conhecidos (CPU 0x01, GPU 0x08).
- Toda escrita é **lida de volta** (ex.: `SetFanMode` → `GetGamingFanBehavior`) e o resultado entra no log.
- Decodificação conforme o confirmado: status no byte 0; ignorar bytes que o ACPI não preenche (lixo de buffer).
- Serialização: uma chamada WMI por vez (lock), com timeout. Leituras com efeito colateral conhecido (`GetGamingSysInfo` grava `PSEE = 1`) ficam documentadas no wrapper.

## 5. Controle das fans
### 5.1 Modelo
- Cada fan (CPU, GPU) tem um **modo**: `Auto do EC`, `Max` ou `Curva`. Cada uma é independente (suportado pelo protocolo da Acer).
- **Curva**: pontos (temperatura °C → %), de 2 a 10, monotônica (não pode descer quando a temperatura sobe). Fonte de temperatura por fan: CPU usa `CTML`; GPU usa a temperatura do NVML, com regra especial quando a dGPU está desligada (§5.3).
- Opção "fans ligadas": a curva da GPU segue a da CPU com um deslocamento.

### 5.2 Laço (1 Hz, ajustável de 0,5 a 2 s)
1. Lê telemetria e valida (§6.2).
2. Para cada fan em `Curva`: interpola a curva → aplica **histerese** (só baixa quando a temperatura cai X °C abaixo do ponto que fez subir; padrão 3 °C) → **rampa** (sobe rápido, por exemplo até +15%/s; desce devagar, por exemplo −3%/s) → **limites** (mínimo e máximo do perfil e o piso de segurança do modo térmico).
3. Grava só se mudou ≥ 1% (evita tráfego no EC).
4. Confere se o modo da fan no firmware ainda é o que o serviço colocou (§7).

### 5.3 GPU com Optimus
Quando a dGPU dorme, o NVML não lê temperatura (o HWiNFO mostrou 0 °C). Nesse caso, a fan da GPU segue a temperatura da CPU com um piso próprio. Nunca tratar "sem leitura" como "frio".

### 5.4 Piso de segurança por modo térmico (valores iniciais; ajustar com o teste A17)
| Modo | Piso CPU | Piso GPU |
|---|---|---|
| Silencioso / Eco | 20% | 20% |
| Equilibrado | 25% | 25% |
| Desempenho | 35% | 35% |
| Turbo | 45% | 45% |
Motivo: no teste de 10/40 em Turbo a CPU subiu de ~65 °C para 83–87 °C em ~4 s com carga curta.

> **Substituído por §5.4.1** (aprovado pelo usuário em 2026-11-05). A tabela acima fica só como histórico.

#### 5.4.1 Revisão depois do A17 — APROVADA (2026-11-05)
O A17 mostrou que o Custom nunca gira abaixo de ~2000 rpm e que 20% já dá ~3150 rpm, enquanto o Auto do EC em repouso fica em ~2040 rpm. Com os pisos acima, o killer-sense seria **mais barulhento que o PredatorSense em repouso**. Proposta:

1. **Modo híbrido por fan ("Curva")**: abaixo de uma temperatura de entrada a fan fica no **Auto do EC** (silencioso e com a proteção do próprio EC); acima dela o serviço assume com a curva.
   - Entrada padrão: CPU **60 °C**, GPU **55 °C** (ajustável por perfil).
   - Volta ao Auto quando a temperatura cair **8 °C abaixo** da entrada **e** a curva já estiver no piso (evita ficar trocando de modo).
2. **Novos pisos (só valem enquanto a fan está em Custom)**:
   | Modo | Piso | RPM aprox. CPU / GPU |
   |---|---|---|
   | Silencioso / Eco / Equilibrado | 0% | ~2200 / ~1870 |
   | Desempenho | 15% | ~2900 / ~2600 |
   | Turbo | 30% | ~3700 / ~3400 |
3. **Sem mudança** na emergência (95/87 °C → Max), nas leituras inválidas (→ Auto), na rampa de subida (+15%/s) nem no watchdog.
4. A interface mostra o RPM esperado de cada ponto da curva usando a tabela do A17.

## 6. Segurança (requisito obrigatório da Fase 3)
### 6.1 Estado seguro = Auto do EC
`SetGamingFanBehavior(0x00410009)`, a única escrita já testada no hardware. O Guardião aplica o estado seguro e confirma por leitura.

### 6.2 Validação da telemetria
- Leitura inválida: 0, fora da faixa física (temperatura < 1 °C ou > 110 °C; RPM > 8000), erro WMI, ou sem mudança há mais de N segundos quando deveria variar.
- Decisão sempre sobre **várias amostras** (mediana das últimas 3).
- **3 leituras inválidas seguidas** de um sensor usado numa curva → aquela fan volta ao Auto e a interface avisa.

### 6.3 Gatilhos de volta ao Auto
| Situação | Ação |
|---|---|
| Parada normal do serviço, desinstalação | Auto antes de sair |
| Exceção não tratada | handler global → Auto → encerra |
| Desligamento / reinício | `PreShutdown` → Auto |
| Suspensão / hibernação | Auto antes de dormir; na retomada recomeça em Auto e só depois reaplica o perfil |
| CPU ≥ 95 °C ou GPU ≥ 87 °C (limite térmico da GPU lido no HWiNFO) | **Max** até cair 10 °C, depois Auto; perfil manual pausado até o usuário reativar |
| Processo do serviço morto à força (sem chance de rodar código) | **Watchdog**: o serviço grava um "batimento" a cada 1 s (memória compartilhada nomeada); se parar por 3 s com fan em manual, o Watchdog aplica Auto. Além disso, o Windows reinicia o serviço (ações de recuperação) e o serviço **sempre começa aplicando Auto** |
| Watchdog também morto | Proteção do próprio hardware: o EC/CPU continuam com limite térmico e throttling. Risco residual aceito e documentado |

O Watchdog só pode chamar uma operação: `SetGamingFanBehavior(0x00410009)`.

## 7. Convivência com o software da Acer (decisão D1)
- O PredatorSense e os serviços dele continuam rodando e **podem escrever** (por exemplo: o app reenvia `FAN_CONTROL` ao abrir; a tecla de modos troca o `OPMS`; o Turbo reaplica o overclock).
- O serviço confere a cada ciclo se o modo da fan e o `OPMS` continuam como ele deixou. Se mudarem por fora:
  - **não briga**: pausa o controle manual, assume o estado novo e avisa na interface ("o PredatorSense alterou as fans");
  - o usuário escolhe se reaplica o perfil.
- Recomendação de uso: não deixar o PredatorSense aberto enquanto o killer-sense controla as fans. Fica documentado.
- O serviço **nunca** lê nem confia no registro da Acer (`CurrentFanMode` fica dessincronizado); a fonte da verdade é o firmware.

## 8. Telemetria
| Dado | Fonte | Observação |
|---|---|---|
| CPU temperatura | EC `GetGamingSysInfo` idx 1 (`CTML`) | CONFIRMADO (A11) |
| RPM CPU / GPU | EC idx 2 (`C1FR`) / idx 6 (`G1FR`) | CONFIRMADO (A4, A10); teto ~6976 |
| Outros sensores | EC idx 3 (`EST1`), idx 10 (`SKTC`) | rótulos a definir; `SKTC` já devolveu 0 inválido |
| GPU temp, hotspot, clocks, potência, uso, VRAM | NVML | sem driver extra |
| CPU uso, clock | contadores do Windows (PDH) | sem driver extra |
| Modo térmico, modo das fans | EC via `GetGamingMiscSetting(0x0B)` e `GetGamingFanBehavior` | |
| Potência da CPU, PL1/PL2 | **adiado** (exige driver de baixo nível, D2) | por ora: não exibido |
Frequência: 1 Hz para a interface; histórico em memória (ex.: 10 min) para gráficos.

## 9. Canal serviço ↔ interface
- Pipe nomeado `\\.\pipe\killer-sense`, ACL: SYSTEM + o usuário interativo atual. Sem TCP (o app da Acer usa TCP local; nós não).
- Mensagens JSON com versão de protocolo. Tipos: `GetState`, `SubscribeTelemetry`, `ApplyProfile`, `SetFanCurve`, `SetMode`, `RestoreAuto`…
- O serviço **valida tudo de novo** (a interface não é confiável). Pedido inválido → erro, nada é aplicado.

## 10. Configuração
- Arquivo JSON em `%ProgramData%\KillerSense\config.json`, gravado só pelo serviço (ACL), com versão de esquema e backup do anterior.
- Validação ao carregar: curvas monotônicas, faixas, pisos de segurança. Arquivo inválido → serviço sobe em Auto e avisa.
- Perfil = modo térmico + modo/curva de cada fan + (fases seguintes) bateria, teclado, offsets da GPU.
- Exportar/importar perfis; perfil "Padrão Acer" somente leitura, montado a partir do que foi mapeado (por exemplo: Turbo = +100/+200 na GPU).

## 11. Logs
- Arquivo rotativo em `%ProgramData%\KillerSense\logs`. Toda escrita no firmware é registrada com valor enviado, resposta e leitura de volta.

## 12. Interface (Avalonia)
### 12.1 Princípios
- **Bonita e própria**: identidade visual do killer-sense, sem copiar a arte da Acer.
- **Leve**: nada roda na interface que não seja visual. Toda lógica e todo acesso a hardware ficam no serviço.
- **Clara sobre segurança**: o estado das fans (Auto do EC / Max / Curva / Emergência) aparece sempre, com destaque, e há um botão "Voltar ao Auto" em todas as telas e no menu da bandeja.

### 12.2 Design system (`KillerSense.UI.Theme`) — a definir no protótipo
- Tema escuro como padrão (claro opcional), uma cor de destaque configurável, tipografia única, ícones vetoriais.
- Controles próprios: **editor de curva** (pontos arrastáveis, temperatura atual marcada na curva, faixa proibida abaixo do piso de segurança), **mostrador** (temperatura/RPM), **gráfico de linha** em tempo real, **seletor de modo** (Silencioso → Turbo), **seletor de cor por zona** do teclado.
- Animações curtas e só por GPU (sem custo quando a janela está parada).

### 12.3 Telas (paridade com o PredatorSense + o que pedimos)
| Tela | Conteúdo | Fase |
|---|---|---|
| Painel | CPU/GPU: temperatura, RPM, %, clock, uso; modo térmico; estado das fans; gráficos dos últimos minutos | 3 |
| Fans | modo por fan (Auto/Max/Curva), editor de curva, opção "fans ligadas", histerese/rampa (avançado) | 3 |
| Modos e perfis | Silencioso/Equilibrado/Desempenho/Turbo (+ Eco na bateria), perfis do usuário, perfil "Padrão Acer" (somente leitura), exportar/importar | 4 |
| Bateria | limite de carga (~80%), estado | 4 |
| Iluminação | cor por zona (4 zonas), efeitos, brilho, liga/desliga por zona, desligar após 30 s | 4 |
| Teclado e sistema | trava da tecla Windows, sticky key, comportamento da tecla de modos | 4 |
| GPU | offsets de clock por perfil (núcleo/memória), com valores padrão da Acer | 4 (último) |
| Configurações | início com o Windows, unidades, tema, logs, sobre | 3 |

### 12.4 Bandeja
Ícone com indicação do estado (Auto/Curva/Emergência), menu com: modo térmico, perfil, "Voltar ao Auto", abrir janela, sair (sair da bandeja **não** para o serviço).

### 12.5 Protótipo antes do código (entrega da Fase 2)
Protótipo v1 (2026-11-05): `docs/prototipo/index.html` (HTML estático, dados simulados). Para ver: `scripts/dev/serve-static.ps1 -Root docs/prototipo` e abrir `http://localhost:5173`. Estilos: **A · Pulso** (escuro, ciano, brilho) e **B · Grafite** (escuro neutro, violeta, sem brilho). É só referência visual; a interface real será feita em Avalonia.
1. Protótipo visual estático das telas **Painel** e **Fans** (2 propostas de estilo para o usuário escolher).
2. Ajustes até aprovação.
3. Só então o design system vira código (na Fase 3, junto com as fans).

### 12.6 Metas de leveza (critérios de aceitação, medidos com o Gerenciador de Tarefas)
| Situação | Meta (RAM somada) | Referência PredatorSense (medido 2026-11-05) |
|---|---|---|
| Janela fechada (serviço + watchdog + bandeja) | **≤ 100 MB** | app 474 MB; tudo da Acer ~1,08 GB (23 processos) |
| Janela aberta no Painel com gráficos | **≤ 200 MB** | — |
| CPU com janela fechada | **< 0,5%** em média | — |
| Abrir a janela | **< 1 s** | — |
Se uma meta não for atingida, é tratado como defeito.

## 13. Riscos em aberto
| Risco | Tratamento |
|---|---|
| Escritas Max/Custom/Speed ainda não testadas no hardware (A15) | primeiro passo da Fase 3, com aprovação |
| `GetGamingSysInfo` grava `PSEE = 1` a cada leitura | aceito (o PredatorSense faz o mesmo); documentado |
| Offset de clock da GPU usa função não pública da NVAPI | fica para a fase da GPU; decidir lá |
| Sem leitura de potência da CPU (D2) | adiado para quando houver o driver |
| Ghidra/engenharia reversa podem ter erro de interpretação | cada item só vira CONFIRMADO após teste no hardware |
| Native AOT no Avalonia exige bindings compilados e cuidado com reflexão | usar `x:CompileBindings` desde o início; medir as metas de leveza a cada entrega |
| Serviço em Native AOT pode não funcionar com o CIM/COM do WMI | o serviço roda normal (JIT); só bandeja e janela em AOT. Reavaliar depois |

## 14. Plano de testes da Fase 3 (só fans)
1. **A15** no hardware, uma escrita por vez, com HWiNFO aberto: Max (`0x00820009`), Custom (`0x00C30009`), `SetGamingFanSpeed` (ex.: 40% CPU), volta ao Auto.
2. **A17**: curva % → RPM de cada fan (valida pisos e a rampa).
3. Testes unitários com firmware simulado: curva, histerese, rampa, validação, todos os gatilhos de failsafe.
4. Stress de CPU e de GPU com curva ativa; acompanhar temperaturas.
5. Matar o serviço à força (`taskkill /f`) com fan em manual → o Watchdog devolve o Auto em ≤ 3 s.
6. Matar serviço **e** Watchdog → serviço reiniciado pelo Windows aplica Auto ao subir.
7. Simular leitura inválida (firmware simulado e, no hardware, sensor forçado a "inválido" por configuração de teste) → fan volta ao Auto.
8. Suspender/hibernar e retomar; desligar e reiniciar → Auto confirmado.
9. Abrir o PredatorSense com curva ativa → o serviço detecta, pausa e avisa (não briga).

10. Medir as metas de leveza (§12.6) com o Painel aberto e com a janela fechada.

## 15. Roteiro
| Etapa | Entrega | Depende de |
|---|---|---|
| Fase 2a | este documento aprovado | usuário |
| Fase 2b | protótipo visual (Painel + Fans, 2 estilos) aprovado | 2a |
| Fase 3.0 | ambiente: SDK .NET 10 + pacotes NuGet (lista para aprovação), solução criada, testes unitários com firmware simulado | 2a |
| Fase 3.1 | A15 no hardware (escritas de fan, uma por vez) e A17 (curva % → RPM) | 3.0 + aprovação de cada escrita |
| Fase 3.2 | serviço + watchdog + laço de fans + failsafe; testes do §14 | 3.1 |
| Fase 3.3 | bandeja + janela (Painel, Fans, Configurações) no estilo aprovado | 2b + 3.2 |
| Fase 4 | modos (A16, A18) → bateria (A8) → iluminação (A9) → teclado/sistema → GPU | 3 |
| Depois | monitoramento extra; driver de baixo nível (potência da CPU) | decisão futura |

## 16. Ambiente e dependências (Fase 3.0)
- **SDK .NET 10.0.403** (runtime 10.0.14), zip portátil da Microsoft em `tools/dotnet` (SHA-512 conferido, assinatura Microsoft). Instalado em 2026-11-05.
- Pacotes NuGet propostos (versões estáveis mais recentes em 2026-11-05; aguardando aprovação):

| Pacote | Versão | Autor | Usado em | Para quê |
|---|---|---|---|---|
| Avalonia | 12.1.5 | Avalonia Team | Tray, App, UI.Theme | interface |
| Avalonia.Desktop | 12.1.5 | Avalonia Team | Tray, App | janela e bandeja no Windows |
| Avalonia.Themes.Fluent | 12.1.5 | Avalonia Team | App | base dos controles (o tema Grafite fica por cima) |
| CommunityToolkit.Mvvm | 8.4.2 | Microsoft | App, Tray | MVVM sem reflexão (compatível com Native AOT) |
| Microsoft.Management.Infrastructure | 3.0.0 | Microsoft | Firmware | WMI/CIM (o mesmo do PowerShell) |
| Microsoft.Extensions.Hosting | 10.0.14 | Microsoft | Service, Watchdog | ciclo de vida do serviço |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.14 | Microsoft | Service, Watchdog | integração com o Gerenciador de Serviços |
| Serilog.Extensions.Hosting | 10.0.0 | Serilog | Service, Watchdog | logs |
| Serilog.Sinks.File | 7.0.0 | Serilog | Service, Watchdog | log em arquivo com rotação |
| xunit.v3 | 4.0.2 | xUnit | Tests | testes |
| xunit.runner.visualstudio | 4.0.1 | xUnit | Tests | rodar testes pelo `dotnet test` |
| Microsoft.NET.Test.Sdk | 18.11.0 | Microsoft | Tests | infraestrutura de testes |

- **Sem biblioteca de gráficos**: mostradores, gráfico de linha e editor de curva serão controles próprios desenhados direto no Avalonia (como no protótipo). Menos dependências, mais leve e com o visual exato do tema.
- **Sem pacote** para NVML, NVAPI e HID: chamadas diretas às DLLs do sistema (`nvml.dll`, `hid.dll`) via `LibraryImport`.
- As versões ficam fixas num arquivo central (`Directory.Packages.props`) e com `packages.lock.json`, para o build ser reproduzível.

## 17. O que a Fase 2 precisa de você
**Aprovado pelo usuário em 2026-11-05:**
- C#/.NET 10 + Avalonia.
- Watchdog como serviço separado.
- Pisos de segurança iniciais (§5.4) e limites de emergência (95 °C CPU / 87 °C GPU).
- Comportamento "não brigar" com o PredatorSense (§7).
- Metas de leveza (§12.6).

**Estilo visual escolhido (2026-11-05): B · Grafite** — escuro neutro, destaque violeta, sem brilho, cantos menores (§12.5). Os tokens do protótipo (`body.b` em `docs/prototipo/index.html`) são o ponto de partida do `KillerSense.UI.Theme`.
