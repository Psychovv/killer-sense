# Interface Acer (PHN16-72) — mapa vivo

Legenda:
- **CONFIRMADO** = visto neste notebook.
- **CONFIRMADO (ACPI)** = está escrito no código ACPI deste notebook, mas ainda não foi executado/medido. O comportamento real (EC/BIOS) pode diferir.
- **SUPOSIÇÃO** = hipótese, não verificada.

## Passo 1 — Identificação (CONFIRMADO, registro HKLM\HARDWARE\DESCRIPTION\System\BIOS)
| Campo | Valor |
|---|---|
| SystemManufacturer | Acer |
| SystemProductName | Predator PHN16-72 |
| SystemFamily | Predator Helios Neo 16 |
| BIOSVendor / BIOSVersion / Data | Insyde Corp. / V1.20 / 08/28/2026 |
| BaseBoard | RPL / EQE_RTX |

## Passo 2 — Classes WMI em root\WMI (CONFIRMADO: existem; metadados lidos sem admin)
Instâncias NÃO puderam ser listadas sem administrador (Get-CimInstance → 0x80041003 Acesso negado). Nenhum método foi chamado.

### AcerGamingFunction (nomes CONFIRMADOS; função de cada um é SUPOSIÇÃO pelo nome)
Entradas: `gmInput` (UInt64, UInt32 ou UInt8[]), saída `gmOutput`.
| Método | Entrada | Saída | Provável função (SUPOSIÇÃO) |
|---|---|---|---|
| Set/GetGamingProfile | UInt64 / UInt32 | UInt32 / UInt64 | perfil térmico |
| Set/GetGamingProfileSetting | idem | idem | parâmetros do perfil |
| Set/GetGamingFanBehavior | idem | idem | modo auto/manual das fans |
| Set/GetGamingFanSpeed | idem | idem | velocidade das fans |
| Set/GetGamingFanTable | UInt64 / (sem entrada no Get) | UInt32 / UInt64 | tabela/curva de fans |
| GetGamingSysInfo | UInt32 | UInt64 | info do sistema (temps/RPM?) |
| Set/GetGamingLED, LEDBehavior, LEDColor | vários | vários | LEDs |
| Set/GetGamingRgbKb, KBBacklight | vários | vários | RGB / backlight do teclado |
| Set/GetGamingMiscSetting | UInt64 / UInt32 | UInt32 / UInt64 | misc (bateria? tecla Predator?) |
| Set/GetCPUOverclockingProfile | OCProfile UInt8 + OCStructure UInt8[] | ReturnCode | perfil de OC da CPU |

### AcerGenericMethod
SetFunction/GetFunction (UInt64), GetStorageInformation (info de SSD). Função de SetFunction/GetFunction: SUPOSIÇÃO (possível bateria/geral).

### Outras
AcerBiosConfigurationTool (Get/SetBiosOptions e Defaults) e AcerGenericEvent (eventos, sem métodos). Não relacionadas a fans; não mexer.

## Passo 3 — Tabelas ACPI
Extraídas sem admin e sem driver, lendo `HKLM\HARDWARE\ACPI` (cópia que o Windows mantém): DSDT + 24 SSDT. Ficam em `acpi-dump/` (ignorado pelo git: conteúdo da Acer, não versionar). Decompiladas com `iasl` 20260408 da Intel (SHA256 conferido) em `acpi-dump/dsl/`.
Obs.: `GetSystemFirmwareTable` devolve só a 1ª SSDT de cada assinatura; por isso foi usado o registro.

### 3.1 Dispositivo WMI de gaming — CONFIRMADO (ACPI)
`\_SB.PC00.WMID` na **SSDD** (`_HID PNP0C14`, `_UID "APGe"`). A tabela `_WDG` liga cada GUID a um método `WMxx`; o Windows liga cada classe ao GUID (qualificador `guid` da classe):

| Classe WMI | GUID | Método ACPI |
|---|---|---|
| AcerGamingFunction | 7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56 | `WMBH` |
| AcerGenericMethod | 1EA0BC57-521E-461B-A7A9-5D36F141A82A | `WMAE` |
| AcerBiosConfigurationTool | 4BB53443-488A-430D-A25C-874660E23BDD | `WMAC` |
| AcerGenericEvent | 2E6B3CE2-6A58-4AF2-8767-4640CA8A38E3 | evento (notify 0xAE) |

Outros GUIDs da SSDD (AA, AB, BE, BF, BK, BL, BG, BP; eventos 0xBC/0xBD) ainda sem classe WMI associada. A SSDI tem outro `PNP0C14` (GUID 603E9613-…, `WMAA`), não relacionado a gaming até agora.

### 3.2 `WMBH(Arg0, Arg1 = WmiMethodId, Arg2 = buffer de entrada)` — CONFIRMADO (ACPI)
`WmiMethodId` lido do qualificador do MOF (CONFIRMADO). Entradas UInt64/UInt32 chegam como buffer de bytes little-endian: `b0` = byte menos significativo.

| Id | Método | O que o ACPI faz |
|---|---|---|
| 1 | SetGamingProfile | **SMI** para o BIOS (`WSMI` → porta SMI `WSSP = 0xD0`). Opaco ao ACPI. |
| 3 | GetGamingProfile | SMI |
| 5 | GetGamingSysInfo | `b0=1`: lê do EC pelo índice `b1` (tabela 3.4). `b0=2`: bit `BBMS`. `b0=0`: retorna versão fixa (0x27,0x02). Saída: `[0]=status`, `[1..2]=valor` (16 bits) |
| 6/7 | Set/GetGamingRgbKb | cor RGB por zona do teclado: `b0` = máscara de zona 1/2/4/8 → `KB1..KB4 R,G,B`; `b0=0,b4=1,b5=1/2` → light bar `LB1/LB2` |
| 8/9 | Set/GetGamingProfileSetting | SMI |
| 10/11 | Set/GetGamingLEDBehavior | `LBET` (light bar) |
| 12/13 | Set/GetGamingLEDColor | `LBLR/LBLG/LBLB/LBLT/LBLF` (light bar) |
| 14/15 | Set/GetGamingFanBehavior | **SMI** (modo auto/manual das fans fica no BIOS/SMM, invisível no ACPI) |
| **16** | **SetGamingFanSpeed** | `b0` = fan (1 = CPU → `CPUF`; 4 = GPU → `GPUF`; 5 = GPU2 → `GPU2`), `b1` = **0..100** (rejeita >100 com status 2). Escreve direto no EC. |
| **17** | **GetGamingFanSpeed** | lê `CPUF`/`GPUF`/`GPU2` conforme `b0` |
| 18/19 | Set/GetGamingFanTable | `FTBL` (1 byte no EC) |
| 20/21 | Set/GetGamingKBBacklight | efeito/brilho/velocidade/cor do teclado (`KBLE, KBLS, KBBP, KBED, KBCR/G/B, KLES`) ou light bar (`LB…`) |
| 22 | SetGamingMiscSetting | `b0=1` → `TKST` (Turbo key?); `b0=0x0B` → `OPMS` (modo de operação) + notifica Intel DTT (`IETM`) e NVIDIA (`NPCF`); `b0=0x0F` → `FMKY` (b1=1 liga, 2 desliga); `b0=2,6,8` → SMI |
| 23 | GetGamingMiscSetting | espelho do 22 (`b0=0x0A` retorna fixo 0x73) |
| 2/4 | Set/GetGamingLED | `b0=1`: `TKST` 0x10/0x11; `b0=8`: `KBST`; `b0=0x10`: `LBST` |
| 24/25 | Set/GetCPUOverclockingProfile | SMI |

### 3.3 Região do EC — CONFIRMADO (ACPI)
`\_SB.PC00.LPCB.EC0` (`PNP0C09`, portas 0x62/0x66) declara, além da região `ERAM` (EmbeddedControl, só `ERBD`@0x5F e `SMPR`@0x60), **duas regiões de memória** `ECMM` e `PECM`, ambas em **0xFE0B0800, 0x1000 bytes**. Ou seja, a RAM do EC também é acessível por MMIO. Lista completa de campos/offsets em `acpi-dump/ec-fields.csv` (gerado do DSDT).

| Offset hex (dec) | Campo | Uso visto no ACPI | Interpretação |
|---|---|---|---|
| 0x11 (17) w16 | `G2FR` | SysInfo idx 9 | RPM fan GPU2? — SUPOSIÇÃO |
| 0x13 (19) w16 | `C1FR` | SysInfo idx 2 | RPM fan CPU — SUPOSIÇÃO |
| 0x15 (21) w16 | `G1FR` | SysInfo idx 6 | RPM fan GPU — SUPOSIÇÃO |
| 0x2C (44) | `OPMS` | MiscSetting 0x0B | modo/perfil de operação — SUPOSIÇÃO forte |
| 0x37 (55) | `CPUF` | FanSpeed fan 1 | % fan CPU (0..100) — CONFIRMADO (ACPI) |
| 0x3A (58) | `GPUF` | FanSpeed fan 4 | % fan GPU (0..100) — CONFIRMADO (ACPI) |
| 0x3B (59) | `GPU2` | FanSpeed fan 5 | % 2ª fan GPU — CONFIRMADO (ACPI) |
| 0x5B (91) | `TKST` | LED/Misc | tecla Turbo/Predator? — SUPOSIÇÃO |
| 0x5C (92) | `FTBL` | FanTable | — |
| 0xB0 (176) | `CTMP` | — | temperatura CPU — SUPOSIÇÃO |
| 0xB1 (177) | `CTML` | SysInfo idx 1 | temperatura CPU — SUPOSIÇÃO |
| 0xB4 (180) | `SKTC` | SysInfo idx 10 | temperatura de sensor/carcaça — SUPOSIÇÃO |
| 0xB6 (182) | `EST1` | SysInfo idx 3 | sensor externo — SUPOSIÇÃO |

**Pistas da comunidade (decimal):** 44, 55, 58, 176 e 180 → **batem** com `OPMS`, `CPUF`, `GPUF`, `CTMP`, `SKTC` — CONFIRMADO (ACPI). 33 (0x21) e 34 (0x22) → **nenhum campo nomeado** no ACPI; se existirem, são internos do EC — DESCARTADO como pista ACPI, não confirmado.

### 3.4 Consequências para o projeto (SUPOSIÇÃO a validar no passo 5)
- Ler temperaturas/RPM: `GetGamingSysInfo` (id 5) é a via limpa, sem driver.
- Fan manual: provavelmente exige `SetGamingFanBehavior` (id 14, via SMI, formato desconhecido) **e** `SetGamingFanSpeed` (id 16, % por fan). O formato do id 14 só sai capturando o PredatorSense.
- Perfis: `SetGamingProfile` (1, SMI) e/ou `SetGamingMiscSetting` b0=0x0B (`OPMS`).
- Não achei ainda limite de bateria 80% no `WMBH`; provável em `AcerGenericMethod` (`WMAE`) — a analisar.
- Via WMI cobre tudo: **não é necessário acesso direto ao EC** até aqui.

### 3.5 Outras classes WMI ligadas ao `_WDG` da SSDD — CONFIRMADO
| GUID | Classe | Método ACPI | Implementação | Uso para nós |
|---|---|---|---|---|
| 79772EC5-… | **BatteryControl** | `WMBE` | SMI | **limite de carga (80%)** — ver 4.4 |
| 61EF69EA-… | APGeAction (Set/GetFunction) | `WMAA` | — | desconhecido |
| 79772EC6-… | LongRunFunction | `WMBF` | — | desconhecido |
| 77B0C3A7-… | S3ResumeFunction | `WMBG` | — | irrelevante |
| 676AA15E-… / A9D77EF6-… | APGeEvent / SecEvent | eventos 0xBC/0xBD | — | eventos (tecla Predator?) — SUPOSIÇÃO |
| 603E9613-… (SSDI) | NvWmiBrightness | `WMAA` (SSDI) | — | brilho NVIDIA, irrelevante |
| F75F5666-… | BIOSSetting | `WMBK` | — | ⛔ **NUNCA CHAMAR** |
| FE1DBBDA-… | UtilityFunction | `WMBL` | — | ⛔ **NUNCA CHAMAR** |
| 9F98130E-… | DataTrans | `WMAB` | — | ⛔ **NUNCA CHAMAR** |
| 48E699DB-… | MethodForPowerShell | `WMBP` | — | ⛔ **NUNCA CHAMAR** |
| 4BB53443-… | AcerBiosConfigurationTool | `WMAC` | — | ⛔ **NUNCA CHAMAR** |

As ⛔ mexem em senha da BIOS, número de série/UUID, ordem de boot, gravação de firmware e defaults da BIOS. O projeto deve **bloquear explicitamente** essas classes (lista de permissão só com AcerGamingFunction, BatteryControl e, se preciso, AcerGenericMethod).

`AcerGenericMethod` → `WMAE`: tudo via SMI (ids 1, 2, 3), formato opaco ao ACPI.

## Passo 4 — Software da Acer instalado (CONFIRMADO, só leitura)
### 4.1 Componentes
| Componente | Tipo | Onde |
|---|---|---|
| PredatorSense 5.0.1341.0 | app **Electron** (UWP/MSIX, `ULICTekInc.PredatorSenseforNotebook`) | `WindowsApps\…\app\PredatorSense.exe` |
| AcerAgentService (`AASSvc`) | serviço nativo C++/Qt6, auto | DriverStore `predatorservice.inf_…\AcerCentralService.exe` → lança `AcerAgentService.exe` |
| AcerHardwareService | processo nativo C++/Qt6 | mesmo pacote; **é quem chama o WMI** |
| AcerLightingService | serviço nativo, auto | mesmo pacote; usa `AcerECKeyboardController.dll`, `AcerECLightbarController.dll` e um **OpenRGB** embutido |
| Acer Care Center (`AcerCCAgent.exe`) | serviço nativo | `System32`; **limite de bateria** |
| Acer Quick Access (`AcerQAAgent.exe`) | serviço nativo | `System32`; USB charge desligado, etc. |
| Tarefa agendada | `\PredatorSenseLauncher` | — |

Nenhum binário relevante é .NET (só um instalador do Electron), então **ilspycmd não se aplica**. Análise feita só por strings.

### 4.2 Como o PredatorSense fala com o firmware (CONFIRMADO por strings)
`PredatorSense (Electron)` → socket/pipe local → `AcerAgentService` → named pipe → `AcerHardwareService` → `ROOT\WMI` → `AcerGamingFunction` / `AcerGenericMethod` → ACPI `WMBH`/`WMAE` → EC ou SMI.
- Comandos internos: `kSvcCmdWMISetGamingFanGroupBehavior`, `…FanGroupSpeed`, `…FanTable`, `…MiscSetting`, `…ProfileConfiguration`, `…SystemInformation`, `kSvcCmdSetOperationMode`, `kSvcCmdWMISetFunction`. O nome "FanGroup" sugere que a entrada de FanBehavior/FanSpeed usa **máscara de grupo de fans** (bate com os ids 1/4/5 do ACPI) — SUPOSIÇÃO.
- As strings de log mostram que o serviço registra `input: 0x%I64X` / `output: 0x%I32X` de cada chamada WMI. Se o log puder ser ligado, ele revela os formatos sem ETW — SUPOSIÇÃO (exigiria escrever no registro; não feito).
- **Não usa driver de porta de I/O para fans.** `inpoutx64.dll` existe no pacote, mas só o OpenRGB o referencia (detecção de SMBus/I2C), e **nenhum driver inpout está carregado**. CONFIRMADO.
- Ajustes de GPU: `.ini` do modelo (`Predator PHN16-72.ini`) só define overclock por perfil: Turbo = +100 (núcleo) / +200 (memória), demais perfis 0, para as GPUs `10DE:2860/28E0/28E1`. SUPOSIÇÃO: aplicado via NVAPI pelo serviço.

### 4.3 Estado atual lido do registro (`HKLM\SOFTWARE\OEM\AcerAgentService`) — CONFIRMADO
`Operation_Mode_Support=1`, `FanControl\CurrentFanMode=0`, `Overclock\GPU_OC_Support=1`, `LightSetting\KeyBoardArea=4` (4 zonas), `EC_HID_Support=0` (não usa o canal HID do EC novo), `Model_Name_1st=Predator PHN16-72`.

### 4.4 Limite de bateria (80%) — CONFIRMADO onde fica
É a função "Battery Boundary Charging" do **Acer Care Center**, via classe `BatteryControl` (`WMBE`, SMI):
- `GetBatteryHealthControlStatus(uBatteryNo, uFunctionQuery, uReserved[]) → uFunctionList, uFunctionStatus[], uReturn[]` (id 20)
- `SetBatteryHealthControl(uBatteryNo, uFunctionMask, uFunctionStatus, uReservedIn[]) → uReservedOut, uReturn` (id 21)
Valores de máscara/status: SUPOSIÇÃO até a captura (passo 5).

## Passo 5 — Leituras reais (Get* puros, admin)
Script: `scripts/recon/snapshot-get.ps1` (lista fixa de 19 chamadas Get* que o ACPI mostra serem leitura pura do EC). Saídas em `recon-out/` (ignorado pelo git).
Excluídos por enquanto: `GetGamingSysInfo` (escreve `EC.PSEE = 1` a cada chamada) e os Get* que viram SMI (3, 9, 15, 25, MiscSetting b0=2/6/8/9).
Efeito colateral inevitável: todo `WMBH` grava `CTID = 0x4842` na memória ACPI compartilhada (`AGNS`), não no EC.

### Formato de retorno — CONFIRMADO
- Saída `UInt64`: byte0 = status (0 = OK), byte1.. = dados.
- Saída `UInt8Array` + `gmReturn`: `gmReturn` = byte0 (status) do buffer ACPI e `gmOutput` = bytes 1..15. Bytes não preenchidos vêm com lixo de chamadas anteriores (o buffer `BHGL` é compartilhado).

### Snapshot 01 — baseline (2026-11-05, PredatorSense aberto, sem mexer)
| Leitura | Valor | Interpretação |
|---|---|---|
| FanSpeed CPU (`CPUF`) | 0x32 = **50** | % da fan CPU — CONFIRMADO lido; se é alvo ou atual: SUPOSIÇÃO |
| FanSpeed GPU (`GPUF`) | 0x3C = **60** | idem |
| FanSpeed GPU2 (`GPU2`) | 0 | não usada neste modelo? — SUPOSIÇÃO |
| FanTable (`FTBL`) | 0 | — |
| Misc 0x0B (`OPMS`) | **6** | modo de operação atual. Projetos Linux sugerem 0=Quiet, 1=Balanced, 4=Performance, 5=Turbo, 6=Eco — SUPOSIÇÃO, a confirmar |
| Misc 0x0F (`FMKY`) | 1 (ligado) | — |
| Misc 0x01 (`TKST`) | 0 | — |
| Misc 0x0A | 0x73 | constante, bate com o ACPI ✔ |
| LED 0x08 (`KBST`) | 7 | — |
| LED 0x10 (`LBST`) | 0 | — |
| RgbKb zona 1 | R=00 G=AE B=C7 | cor da zona 1 (ciano) |
| RgbKb zonas 2–4 | 00 00 00 | — |
| KBBacklight teclado | efeito `KBLE`=3, vel. `KBLS`=5, `KBBP`=0, dir. `KBED`=1, cor 000, `KLES`=3 | — |
| KBBacklight light bar | tudo 0 | — |
| LEDBehavior (`LBET`) | 4 | — |
| LEDColor light bar | R/G/B/T = 0, F=3 | — |

Estado informado pelo usuário no momento do snapshot 01: modo **Eco/Quiet**, fans **Auto**, luz do teclado **desligada**.
- `OPMS = 6` com modo Eco/Quiet → coerente com 6 = Eco (ou Quiet). Falta separar os dois.
- Fans em Auto, mas `CPUF = 50` e `GPUF = 60` → esses campos **não** são a rotação atual em Auto; provavelmente o último valor manual/alvo. SUPOSIÇÃO forte. RPM real deve vir do `GetGamingSysInfo` (`C1FR`/`G1FR`).
- Teclado desligado com `KBBP = 0` → no Eco o brilho fica em 0 (ver snapshot 02). A cor da zona 1 (ciano) fica guardada mesmo com a luz apagada.

### Snapshot 02 — usuário mudou só o modo para **Balanced** (comparar: `scripts/recon/diff-snapshots.ps1`)
| Leitura | 01 (Eco/Quiet) | 02 (Balanced) | Conclusão |
|---|---|---|---|
| Misc 0x0B `OPMS` | 6 | **1** | **CONFIRMADO: Balanced = 1** |
| KBBacklight `KBBP` | 0 | **0x64 = 100** | ver nota abaixo |
| LEDColor `LBLT` | 0 | **0x64 = 100** | ver nota abaixo |

- **CONFIRMADO: Eco = 6** (o usuário confirmou que o modo anterior era Eco). O **Eco só aparece no app fora da tomada**. Na tomada o app mostra Silencioso, Equilibrado, Desempenho e Turbo.
- **CONFIRMADO (2026-11-05, na tomada): Desempenho = 4** (`snap-06-desempenho.json`). O `GetGamingProfile(0)` não muda com o modo.
- **CONFIRMADO: Turbo (pelo app) = 5** (`snap-07-turbo-app.json`). No Turbo pelo app, as fans **continuam em Auto** (`0x41`, `CurrentFanMode = 0`) e `TKST = 0`.

- **CONFIRMADO: Silencioso = 0** (`snap-10-silencioso.json`). Fans em Auto; brilho do teclado (`KBBP`) continua 100 → só o **Eco** zerou o brilho.

**Tabela final de modos (`OPMS`, lido via `GetGamingMiscSetting(0x0B)`) — CONFIRMADO:**
| Modo no app | `OPMS` | Disponível |
|---|---|---|
| Silencioso | 0 | na tomada |
| Equilibrado | 1 | sempre |
| Desempenho | 4 | na tomada |
| Turbo | 5 | na tomada |
| Eco | 6 | só na bateria |
Nenhum modo mexeu no comportamento das fans (sempre `0x41` Auto) nem nos % do Custom. Como trocar o modo (escrita) ainda é o teste A16.

### Teste A13 — overclock da GPU no Turbo (`nvidia-smi`, sem admin)
| | Silencioso | Turbo (app) |
|---|---|---|
| P-state | P4 | **P0** |
| Clock atual GPU / memória | 1110 / 5920 MHz | 1980 / **8201 MHz** |
| Clock máx. GPU / memória (nvidia-smi) | 3105 / 8001 MHz | 3105 / 8001 MHz |
| Registro `Overclock` | `GPU_OC_Support = 1` | igual (não guarda o offset) |
- **CONFIRMADO: +200 MHz na memória** no Turbo: memória em 8201 MHz, acima do máximo nominal de 8001 MHz. Bate com `RAM_Turbo=200` do `.ini` do modelo.
- O `clocks.max` do nvidia-smi **não** reflete o offset. O **+100 no núcleo** (`Turbo=100` no `.ini`) não aparece em repouso — SUPOSIÇÃO forte, pelo mesmo mecanismo. Confirmar exigiria carga na GPU (fora do escopo agora).
- O offset é aplicado pelo **serviço da Acer, no driver NVIDIA**, não pelo firmware (nada mudou no EC). SUPOSIÇÃO forte: via NVAPI. Para o nosso programa, isso é a parte "GPU" da Fase 4 e não depende do WMI da Acer.
- O Turbo também leva a GPU para P0 mesmo em repouso — SUPOSIÇÃO: efeito do perfil, não do offset.

### Teste A12 — tecla de modos (perto do alto-falante, fora do teclado)
- O app tem uma opção para o comportamento da tecla (diálogo "Tecla comutação de modo", depois na página Cenário):
  - **Comutação ciclo de modo:** na tomada, Silencioso → Equilibrado → Desempenho → Turbo; na bateria, Equilibrado ↔ Eco.
  - **Ligar/deslig Turbo:** cada toque liga ou desliga o Turbo (só na tomada). É a opção atual.
- Com **Ligar/deslig Turbo**: a tecla alternou Equilibrado ↔ Turbo. Com o app mostrando Turbo, o EC leu `OPMS = 5`, fans em Auto, `TKST = 0`: **idêntico ao Turbo pelo app** (`snap-09-turbo-tecla.json` × `snap-07`). CONFIRMADO.
- Conclusão: a tecla **não** muda o modo sozinha no firmware. O EC/BIOS avisa o software, e o **serviço da Acer** aplica o modo. Strings do serviço reforçam: `SendTurboKeyPressedEvent`, `kAdmAgtTurboKeyPressed`. SUPOSIÇÃO forte: o aviso chega como evento WMI (`AcerGenericEvent` / `APGeEvent`). **O nosso programa vai precisar escutar esse evento** e aplicar o modo ele mesmo.
- Uma leitura (snapshot 08) pegou `OPMS = 1` com o app em Equilibrado: o toque anterior não levou ao Turbo. Não há indício de problema; foi descartada.
- Pendente, **baixa prioridade** (decisão do usuário em 2026-11-05): ver se a opção de comportamento da tecla fica no EC (`FMKY`?) e capturar o evento WMI da tecla. Fica para depois dos outros testes. Pela hipótese dos projetos Linux, Quiet = 0, Performance = 4, Turbo = 5 — SUPOSIÇÃO até testar.
- `KBBP`/`LBLT` foram para 100. A luz parecia apagada só porque o backlight desliga sozinho após inatividade; ao digitar, ela **acendeu** (informado pelo usuário). Então `KBBP` = **brilho do teclado (0–100)** continua sendo a melhor hipótese (SUPOSIÇÃO forte), e o modo **Eco zera o brilho** e o Balanced o restaura para 100 (SUPOSIÇÃO forte). `LBLT` provavelmente é o brilho da light bar (SUPOSIÇÃO).
- O estado "aceso/apagado por inatividade" não aparece em nenhum campo lido até agora.
- Performance/Turbo não podem ser testados na bateria (o PredatorSense bloqueia sem o carregador), o que bate com `AC_Support = 9` e com a checagem de "ACStatus" vista nas strings do serviço.
| byte2 de quase todas as saídas UInt64 | 00 | 06 | **lixo de buffer** (o ACPI não preenche esse byte; o `BHGK` é compartilhado com as chamadas do PredatorSense). CONFIRMADO: ler só os bytes que o ACPI preenche |
| fans, FTBL, FMKY, RGB, LBET | — | iguais | o modo não mexe nesses campos |

### Snapshot 03 — fans em **Custom CPU 40% / GPU 30%** (modo Balanced, bateria)
| Leitura | 02 (Auto) | 03 (Custom 40/30) | Conclusão |
|---|---|---|---|
| FanSpeed CPU `CPUF` | 50 | **40** | **CONFIRMADO: `CPUF` = % Custom da fan CPU** |
| FanSpeed GPU `GPUF` | 60 | **30** | **CONFIRMADO: `GPUF` = % Custom da fan GPU** |
| FanSpeed GPU2 `GPU2` | 0 | 0 | não usada pelo PredatorSense neste modelo — SUPOSIÇÃO forte |
| Registro `FanControl\CurrentFanMode` | 0 | **2** | 0 = Auto, 2 = Custom — CONFIRMADO; 1 = Max? — SUPOSIÇÃO |
| LED 0x08 `KBST` | 0x07 | 0x0F | mudou o bit 3; provável relação com a luz acesa (usuário digitou), não com as fans — SUPOSIÇÃO |
| RgbKb zonas 2–4 | 00 00 00 | 00 AE C7 (ciano) | as 4 zonas agora estão na mesma cor; o app provavelmente regrava as zonas quando a luz acende — SUPOSIÇÃO |
| byte2 "lixo" | 06 | 0D | confirma de novo que é sobra de buffer |

Conclusões:
- Os 50/60 do baseline (em Auto) eram **valores Custom antigos** guardados no EC. Em Auto, `CPUF`/`GPUF` **não** mostram a rotação real. CONFIRMADO.
- Ler `CPUF`/`GPUF` não diz se o modo é Auto ou Custom. Esse modo fica no `SetGamingFanBehavior` (SMI) e só aparece no registro do serviço da Acer. Para o nosso programa, o **modo da fan precisa vir de `GetGamingFanBehavior`** (teste A7).
- Mesmo com as fans em Custom, o firmware aceitou o modo **na bateria**.

### Snapshot 04 — fans de volta ao **Auto** (teste A5)
| Leitura | 03 (Custom 40/30) | 04 (Auto) | Conclusão |
|---|---|---|---|
| `CPUF` / `GPUF` | 40 / 30 | **40 / 30** (iguais) | ao voltar ao Auto, o app **não** mexe nos % do EC. CONFIRMADO |
| Registro `CurrentFanMode` | 2 | **0** | CONFIRMADO: 0 = Auto |
| demais campos | — | só mudou o "lixo" de buffer | — |

**Conclusão importante para a Fase 3 (segurança):**
- A troca Auto ↔ Custom **não aparece em nenhum campo do EC visível pelo ACPI**. Ela passa só pelo `SetGamingFanBehavior` (SMI). CONFIRMADO por eliminação: nada mais mudou nos snapshots 02→03→04.
- Portanto **restaurar o automático = chamar `SetGamingFanBehavior` com o valor de "Auto"**. Zerar ou mudar `CPUF`/`GPUF` **não** devolve o controle ao EC.
- Sem conhecer o formato de `SetGamingFanBehavior`/`GetGamingFanBehavior`, **não dá para cumprir a regra "sempre restaurar o Auto"**. O teste A7 (ou capturar a chamada do PredatorSense) é **pré-requisito obrigatório** da Fase 3.

### Teste A7 — Get* via SMI (`scripts/recon/snapshot-smi.ps1`)
#### smi-01: fans em Auto, modo Balanced
| Chamada | Entrada | Saída (bytes LSB→) | Leitura |
|---|---|---|---|
| GetGamingFanBehavior | 0x00 | `02 00 …` | status 2 = entrada inválida |
| GetGamingFanBehavior | 0x01 | `00 01` | status 0, valor 0x01 |
| GetGamingFanBehavior | 0x04 | `00 00` | status 0, valor 0 (nenhuma fan nessa posição) |
| GetGamingFanBehavior | 0x05 | `00 01` | 0x0001 |
| GetGamingFanBehavior | 0x08 | `00 40` | 0x0040 |
| GetGamingFanBehavior | 0x09 | `00 41` | 0x0041 |
| GetGamingProfile | 0x00 | `00 FF 01 01 00 FF 01 00` | status 0; significado desconhecido |
| GetGamingProfile | 0x01 | `02 …` | status 2 = entrada inválida |
| BatteryControl.GetBatteryHealthControlStatus | uReserved com 5 bytes | erro "Parâmetro inválido" | o MOF exige `uReserved` MAX=2 → corrigido no script |

**Formato de `GetGamingFanBehavior` — SUPOSIÇÃO forte (bate com os 6 casos):**
- entrada = **máscara de fans**: bit 0 = CPU, **bit 3 (0x08) = GPU**. Bit 2 (0x04) não tem fan.
- saída: byte0 = status; bytes 1–2 = campo de 16 bits com **2 bits por fan, na posição 2×(bit da máscara)**: CPU nos bits 0–1, GPU nos bits 6–7.
- em Auto, as duas fans valem **1** → **1 = Auto**. O valor de Custom (e de Max) deve aparecer no smi-02.
- ⚠️ a máscara de **behavior** (GPU = 0x08) é diferente do id de **speed** (GPU = 0x04, ACPI `WMBH` id 16). Não confundir.

#### smi-02: fans em Custom 40/30 (registro `CurrentFanMode = 2`)
| Chamada | Entrada | smi-01 (Auto) | smi-02 (Custom) |
|---|---|---|---|
| GetGamingFanBehavior | 0x01 | `00 01` | `00 03` |
| GetGamingFanBehavior | 0x05 | `00 01` | `00 03` |
| GetGamingFanBehavior | 0x08 | `00 40` | `00 C0` |
| GetGamingFanBehavior | 0x09 | `00 41` | `00 C3` |
| GetGamingProfile | 0x00 | `00 FF 01 01 00 FF 01 00` | `00 FF 03 01 00 FF 01 00` |
| BatteryControl.GetBatteryHealthControlStatus | bat 1, query 1, uReserved[2] | (erro, entrada errada) | `uFunctionList = 03`, `uFunctionStatus = 00 00 FF 00 00`, `uReturn = 00 00` |

**CONFIRMADO (lido em Auto e em Custom):**
- `GetGamingFanBehavior`: entrada = máscara (CPU 0x01, GPU 0x08). Saída = 2 bits por fan na posição 2×bit: **1 = Auto, 3 = Custom**.
- `GetGamingProfile(0)` byte2 também foi de 01 → 03, então ele repete o modo da fan da CPU. O resto ainda é desconhecido.

**SUPOSIÇÃO forte:**
- **2 = Max** (por eliminação; o app usa `mode` 0/1/2 = Auto/Max/Custom, e o Max ainda não foi testado, A4).
- **Formato do `SetGamingFanBehavior`** (UInt64): `(modos << 16) | máscara`, com os modos no mesmo formato do Get:
  - Auto CPU+GPU = `0x00410009`
  - Custom CPU+GPU = `0x00C30009`
  - Max CPU+GPU = `0x00820009`
  Isso bate com o formato do Get e com o que o driver `acer-wmi` do Linux usa como valores de turbo e restauração (usado só como pista; nada copiado). A confirmar com o primeiro teste de escrita.
- Bateria: `uFunctionList = 03` → duas funções suportadas (bit0 = limite de carga ~80%? bit1 = calibração?); `uFunctionStatus[0] = 0` → limite **desligado** agora? Falta confirmar com o estado do Care Center (teste A8).

Log do app (`%LOCALAPPDATA%\Packages\ULICTekInc.PredatorSenseforNotebook_…\LocalCache\Roaming\acerPredatorSense\logs\predatorsense.log`): mostra só os comandos de alto nível (`FAN_CONTROL {"mode":0|2, "custom_fan_data":[…]}`), sem o hex do WMI. Confirma que os 50/60 do baseline vinham de um Custom antigo, de 2026-11-03.

### Escrita 01 — `SetGamingFanBehavior(0x00410009)` (aprovada pelo usuário, HWiNFO aberto)
Script: `scripts/recon/write-fan-auto.ps1` (uma única chamada Set, valor fixo). Resultado em `recon-out/write-01-fan-auto.json`, 2026-11-05 18:54:36.
| | Valor |
|---|---|
| `GetGamingFanBehavior(0x09)` antes | `0xC300` → CPU=3, GPU=3 (Custom) |
| `SetGamingFanBehavior` saída | `0x0` (status 0 = OK) |
| `GetGamingFanBehavior(0x09)` depois | `0x4100` → CPU=1, GPU=1 (**Auto**) |
| Registro `CurrentFanMode` depois | 2 (o PredatorSense não ficou sabendo da troca) |

- **CONFIRMADO pelo firmware:** formato do Set = `(modos << 16) | máscara`; `0x00410009` devolve CPU e GPU ao Auto.
- O usuário relatou que "os RPM mudaram". Porém o log do HWiNFO exportado às 19:03 (`Downloads\teste_fan.CSV`, 622 colunas) **não tem nenhum sensor de fan/RPM**: o HWiNFO não lê as fans do EC deste modelo na configuração atual. Então **a mudança física ainda não foi medida**. CONFIRMADO só pelo firmware (`0x41`).
- Temperaturas no mesmo log (bateria, em repouso): CPU inteira 54–55 °C, núcleo máximo 48 °C; dGPU 0 °C (desligada pelo Optimus). Sem risco térmico.
- Consequência: para medir RPM, precisamos do `GetGamingSysInfo` (`C1FR`/`G1FR`, teste A6). Ligar o "EC support" do HWiNFO é possível, mas faz ele acessar o EC por driver próprio, concorrendo com o firmware — **não recomendado** sem avaliar.
- Consequência para o projeto: o estado do PredatorSense (registro/UI) pode ficar **dessincronizado** do firmware quando outro programa escreve. Nosso serviço deve sempre ler o estado do firmware (`GetGamingFanBehavior`) e nunca confiar no registro da Acer.

### Teste A6 — `GetGamingSysInfo` (aprovado pelo usuário; grava `EC.PSEE = 1` a cada chamada)
Script: `scripts/recon/read-sysinfo.ps1`. Resultado em `recon-out/sysinfo-01.json`, 2026-11-05 19:07, bateria, Balanced, fans em Auto (após a escrita 01).
| Índice | Campo EC | Leituras (3 × 2 s) | Interpretação |
|---|---|---|---|
| 1 | `CTML` | 61, 61, 61 | temperatura da CPU em °C — SUPOSIÇÃO forte (HWiNFO deu 54–55 °C 4 min antes) |
| 2 | `C1FR` | 2097, 2097, 2083 | **RPM da fan da CPU** — SUPOSIÇÃO forte (faixa típica, varia pouco) |
| 3 | `EST1` | 47, 48, 47 | temperatura de outro sensor (°C) — SUPOSIÇÃO |
| 6 | `G1FR` | 1734, 1734, 1734 | **RPM da fan da GPU** — SUPOSIÇÃO forte |
| 9 | `G2FR` | 0 | não há 2ª fan de GPU — SUPOSIÇÃO forte |
| 10 | `SKTC` | 51, 51, 51 | temperatura de carcaça/sensor (°C) — SUPOSIÇÃO |
| grupo 0 | versão | bytes 3–4 = `27 02` | bate com o ACPI (0x27, 0x02) — CONFIRMADO |
| grupo 2 | `BBMS` | 0 | — |

- Formato CONFIRMADO: entrada `byte0 = 1, byte1 = índice`; saída `byte0 = status`, `bytes 1–2 = valor de 16 bits`.
- **As fans não pararam** depois da escrita 01: em Auto, ~2100 RPM (CPU) e ~1730 RPM (GPU).
- Para confirmar que `C1FR`/`G1FR` são RPM: pôr Custom com valores bem diferentes (ex.: 30% e 70%) e ver os dois números mudarem na mesma direção — falta fazer.
- Para confirmar que `CTML` é a temperatura da CPU: comparar ao mesmo tempo com o "CPU Inteira" do HWiNFO — falta fazer.

### Teste A4 — fans no **Max** pelo PredatorSense (2026-11-05 19:17, bateria, Balanced)
Só leituras: `smi-03-max.json`, `snap-05-max.json`, `sysinfo-02-max.json`.
| Leitura | Auto (19:07) | Max (19:17) | Conclusão |
|---|---|---|---|
| `GetGamingFanBehavior(0x09)` | `0x41` | **`0x82`** | **CONFIRMADO: 2 = Max** (CPU bits 0–1 = 2, GPU bits 6–7 = 2) |
| `GetGamingProfile(0)` byte2 | 01 | 02 | CONFIRMADO: repete o modo da fan da CPU (1/2/3 = Auto/Max/Custom) |
| `CPUF` / `GPUF` (EC %) | 40 / 30 | 40 / 30 | o Max **ignora** os % do Custom e não os altera. CONFIRMADO |
| `C1FR` (SysInfo 2) | ~2090 | **6976** | **CONFIRMADO: RPM da fan da CPU** |
| `G1FR` (SysInfo 6) | ~1730 | **6976** | **CONFIRMADO: RPM da fan da GPU** |
| `CTML` | 61 | 51–52 | caiu ~10 °C com as fans no máximo → reforça que é **temperatura da CPU** (SUPOSIÇÃO forte) |
| `EST1` / `SKTC` | 47–48 / 51 | 43 / 41 | também caíram → sensores de temperatura (°C) |
| Registro `CurrentFanMode` | 0 | **1** | CONFIRMADO: 0 = Auto, 1 = Max, 2 = Custom (numeração do app, diferente da do firmware) |

**Resumo dos modos de fan:**
| Modo | Firmware (2 bits/fan) | `SetGamingFanBehavior` CPU+GPU | App/registro |
|---|---|---|---|
| Auto | 1 | `0x00410009` (escrita testada ✔) | 0 |
| Max | 2 | `0x00820009` (SUPOSIÇÃO forte, não testado) | 1 |
| Custom | 3 | `0x00C30009` (SUPOSIÇÃO forte, não testado) | 2 |
- Max ≈ **6976 RPM** nas duas fans (parece ser o teto). Útil como limite superior de validação da telemetria.

## Testes adiados (fazer quando possível)
| # | Teste | Requisito | Por que foi adiado | O que esperamos ver |
|---|---|---|---|---|
| ~~A1~~ | ~~Desempenho~~ | — | **feito 2026-11-05** | `OPMS = 4` |
| ~~A2~~ | ~~Turbo pelo app~~ | — | **feito 2026-11-05** | `OPMS = 5` |
| ~~A3~~ | ~~Silencioso~~ | — | **feito 2026-11-05** | `OPMS = 0` |
| ~~A4~~ | ~~Fans no **Max**~~ | — | **feito em 2026-11-05 19:17** | Max = 2; RPM 6976 |
| A5 | Voltar as fans para **Auto** depois de Custom/Max | — | depende do teste de Custom | o que muda (ou não) ao voltar ao Auto |
| A6 | `GetGamingSysInfo` (temperaturas e RPM) | **aprovação do usuário** | escreve `EC.PSEE = 1` a cada chamada | RPM real e temperaturas |
| A7 | Get* que viram SMI (3, 9, 15, 25; MiscSetting 2/6/8/9; BatteryControl) | aprovação do usuário | comportamento opaco ao ACPI | formato de FanBehavior, perfil e bateria |
| A8 | Limite de bateria 80% no Care Center (liga/desliga) | depois do A7 | precisa dos Get de BatteryControl | campos de `GetBatteryHealthControlStatus` |
| A9 | Teclado: trocar cor, efeito e brilho | — | ainda não testado | confirmar `KBBP` = brilho, `KBLE` = efeito |
| A10 | Custom com valores bem diferentes (ex.: CPU 30%, GPU 70%) | — | confirmar RPM por fan | `C1FR` cai, `G1FR` sobe |
| A11 | `CTML` × HWiNFO "CPU Inteira" no mesmo instante | HWiNFO aberto | confirmar sensor | valores próximos |
| A12 | Tecla de modos: opção da tecla (`FMKY`?) e evento WMI | na tomada | **baixa prioridade** (usuário) | o essencial já foi feito: a tecla só avisa o software |
| ~~A13~~ | ~~GPU OC do Turbo~~ | — | **feito 2026-11-05** | memória +200 confirmado; núcleo +100 sem carga não aparece |
| A14 | Funções extras do PredatorSense: desligar backlight após 30 s, trava da tecla Windows, sticky key, LCD overdrive, modo de som DTS, USB charging desligado (Quick Access) | — | mapear se entram na paridade | qual método/campo muda em cada uma |
| A15 | Escritas restantes de fan: `SetGamingFanBehavior` Max (`0x00820009`) e Custom (`0x00C30009`), `SetGamingFanSpeed` | HWiNFO aberto, aprovação por escrita | só Auto foi testado | firmware lê 0x82 / 0xC3; RPM muda |
| A16 | Escritas de perfil (`SetGamingMiscSetting` 0x0B × `SetGamingProfile`) | aprovação por escrita | qual dos dois o app usa | `OPMS` muda e o DTT/NVIDIA reagem |

**Objetivo do mapeamento (definido pelo usuário em 2026-11-05):** paridade com todas as funções do PredatorSense antes da Fase 2. Recursos fora do PredatorSense (monitoramento extra etc.) ficam para depois. Fora do escopo, por risco: logo de boot da BIOS e tudo que use as classes ⛔.

## Pendente
- `GGSI` (SysInfo): índices 4,5,7,8,11–17 vazios.
- Formato de entrada de `SetGamingFanBehavior` e `SetGamingProfile` (SMI): passo 5 (captura).
- Tudo marcado "CONFIRMADO (ACPI)" precisa de uma leitura real (Get*, admin) para virar CONFIRMADO.
