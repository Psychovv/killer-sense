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

### Como o overclock da GPU é aplicado (strings do `AcerHardwareService`) — CONFIRMADO por strings
- Usa a **NVAPI** da NVIDIA: `NvAPI_GPU_GetPstates20` / **`NvAPI_GPU_SetPstates20`** (offset de clock por P-state), `NvAPI_GPU_GetAllClockFrequencies`, `NvAPI_GPU_GetThermalSettings`, `nvapi_QueryInterface`.
- Tem nomes de offset para cada modo: `GPU Quiet/Balanced/Performance/Turbo/Extreme/Default overclock frequency` e os mesmos com `RAM_`. O `.ini` deste modelo só preenche o Turbo (+100 núcleo / +200 memória); os outros são 0. "Extreme" não aparece no app deste modelo.
- ⚠️ `SetPstates20` **não faz parte do SDK público** da NVAPI (é acessado via `nvapi_QueryInterface`). Usá-lo no nosso programa exige pesquisar a interface; risco técnico a avaliar na Fase 2. Alternativa pública: travar faixas de clock (`nvidia-smi -lgc/-lmc`), que não é a mesma coisa que offset.

### Limites de potência no Turbo (HWiNFO, `teste_fan2.CSV`, 20:08)
| | Valor |
|---|---|
| CPU PL1 (estático/dinâmico) | 115 W / 115 W |
| CPU PL2 (estático/dinâmico) | 157 W / 157 W |
| GPU limite de potência nominal / máximo | 80 W / 140 W |
| GPU limite térmico | 87 °C |
Os outros modos ainda não foram medidos → teste A18.

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

### Testes A10 + A11 — Custom CPU 10% / GPU 40% (2026-11-05 20:08, na tomada)
Leituras: `snap-11-custom-10-40.json`, `smi-11-custom-10-40.json`, `sysinfo-11-custom-10-40.json`; HWiNFO: `Downloads\teste_fan2.CSV`.
O usuário pediu 10/40 (menos barulho). ⚠️ O modo **não saiu do Turbo** (`OPMS = 5`), ao contrário do combinado.

| Leitura | Valor | Conclusão |
|---|---|---|
| `CPUF` / `GPUF` | 10 / 40 | o app gravou os % pedidos (10% foi aceito) |
| `GetGamingFanBehavior(0x09)` | `0xC3` | Custom nas duas |
| `C1FR` | 3846 → 3658 → 3614 | caindo (ainda desacelerando em ~4 s) |
| `G1FR` | 4918 | estável |
| `SKTC` | **0**, 56, 56 | ⚠️ **leitura inválida esporádica** (0) |

- **A10 CONFIRMADO:** em Auto a CPU girava mais que a GPU (2090 × 1730); com 10/40 a ordem se inverteu (3614 × 4918). Então `C1FR` = fan da CPU e `G1FR` = fan da GPU.
- A relação % → RPM **não é linear nem imediata**: 10% ainda dava ~3600 RPM caindo, e 40% dá ~4900 RPM. Pode haver um mínimo imposto pelo EC/Turbo. SUPOSIÇÃO; mapear a curva % → RPM depois, com o modo fixo.

**A11 — `CTML` × HWiNFO (mesmos instantes):**
| Hora | `CTML` (EC) | HWiNFO "CPU Inteira" (DTS) | HWiNFO "CPU Inteira" (Enhanced) |
|---|---|---|---|
| 20:08:29 | 83 | 87 | 83 |
| 20:08:31 | 70 | 77 | 74 |
| 20:08:33 | 68 | 69 | 72 |
- **CONFIRMADO: `CTML` = temperatura da CPU** (mesma tendência, diferença de 0–7 °C; o EC parece amostrar/suavizar diferente).
- O pico de 83–87 °C às 20:08:29 coincide com o início do nosso script (PowerShell + WMI geram carga). Houve outro pico às 20:08:47 (~80 °C). Com a fan da CPU a 10% em Turbo, a CPU sobe rápido mesmo com carga curta.

**Lições de segurança para a Fase 3 (vindas deste teste):**
1. Telemetria do EC às vezes devolve **0** (visto em `SKTC`); a dGPU também lê 0 °C quando desliga (Optimus). O failsafe deve tratar 0 e valores fora da faixa como **inválidos**, nunca como "frio".
2. Decidir por **mais de uma amostra** (mediana/média curta), nunca por uma leitura isolada.
3. Picos de carga curtos elevam a CPU em 15–20 °C em ~4 s com fan baixa → o mínimo da fan da CPU precisa considerar o **modo térmico** (Turbo exige mínimo maior) e o laço precisa reagir em segundos.
4. Conferir sempre o modo térmico antes de colocar fan baixa (aqui o Turbo ficou ativo sem ninguém perceber).

## Engenharia reversa (aprovada pelo usuário em 2026-11-05, só para interoperar)
Material extraído/descompilado fica em `re/` (ignorado pelo git). Aqui entra só a descrição da interface.

### App PredatorSense (Electron, `app.asar`) — extraído com `scripts/re/extract-asar.ps1`
- O app conversa com o serviço por **TCP local `127.0.0.1:51779`** (módulo "ADES"), com comandos JSON de alto nível (`SET_DEVICE_DATA`, `GET_UPDATED_DATA`…). A UI roda em `https://localhost:6888`.
- Enumerações internas (CONFIRMADO no código, batem com as medições):
| Enum | Valores |
|---|---|
| `OPERATING_MODE` | QUIET=0, DEFAULT=1, **EXTREME=4** ("Desempenho"), TURBO=5, ECO=6 |
| `FAN_MODE` (app/registro) | Auto=0, Max=1, Custom=2 |
| `OVERCLOCKING_MODE` | Default=0, Extreme=1, Turbo=2 |
| `BATTERY_CHARGE_MODE` | OptimizedBatteryCharging=0 (limite ~80%?), FullBatteryCharging=1, AdaptiveBatteryCharging=2 |
| `KEYBOARD_LED_TYPE` | PERKEY=0, ONE_AREA=1, THREE_AREA=3, FOUR_AREA=4 |
| `KEYBOARD_ZONE_INDEX` | zone1..zone4 = 0..3 |
| `LIGHTING_MODE` | GLOBAL=0, AREA=1 |
| `EFFECT_DIRECTION` | RIGHT=1, LEFT=2, UP=3, DOWN=4, CLOCK=5, ANTICLOCK=6, RIGHTUP=7, LEFTUP=8 |
| `DTS_SOUND_MODE` | MUSIC=0, MOVIE=1, VOICE=2, STRATEGY=3, RPG=4, SHOOTER=5, CUSTOM_AUDIO=6, AUTOMATIC=10 |
| `RETURN_CODE` | SUCCESS=0, FAIL_TO_GET_DATA=1, TIMEOUT=2, WRONG_PARAMETERS=3 |
- **Lista de funções do app (base da paridade):** `OPERATING_MODE`, `FAN_CONTROL`, `OVERCLOCKING_MODE`, `GPU_MODE` (MUX/Optimus? registro `dgpu_mode_capability = 7`), `BATTERY_BOOST`, `USB_CONTROL`, `LCD_OVERDRIVE`, `BACKLIGHT_30S`, `STICKY_KEY`, `WIN_KEY`, `BOOT_SOUND`, `SOUND_MODE` (DTS), `CUSTOM_BOOT_LOGO` (⛔ fora do escopo), efeitos de teclado (`MAG_STATIC/BREATHING/NEON/WAVE/SNAKE/RAINBOW/BLASTING/SPOT/STAR/SLASH/…`, `KEYBOARD_STATIC/DYNAMIC`), light bar / logo (provavelmente não existem neste modelo), cenários (`SWITCH_PROFILE`, `APPLY_PROFILE`), e monitoramento (`CPU_TEMPERATURE`, `CPU_USAGE`, `CPU_FANSPEED`, `GPU_*`, `SYS_TEMPERATURE`, `RAM_USAGE`).
- O app **não** contém os formatos WMI: eles ficam no `AcerHardwareService` (próximo passo: Ghidra).

### Serviços nativos (Ghidra 12.2 headless; `scripts/re/run-ghidra.ps1` + `scripts/re/ghidra/*.java`)
Análise só de cópias em `re/bin`. Decompilados em `re/decomp` (não versionados). Abaixo: só o protocolo.

**Arquitetura (CONFIRMADO no código):** app → TCP `127.0.0.1:51779` → `AcerAgentService` (monta os valores) → pipe → `AcerHardwareService` (repassa o UInt64 ao WMI, sem alterar) → `AcerGamingFunction`.

**Modo de operação — `kSvcCmdSetOperationMode` (AcerHardwareService):**
- `SetGamingMiscSetting(gmInput = (modo << 8) | 0x0B)`, com `modo` = valor do `OPMS` (0, 1, 4, 5, 6). Ex.: Turbo = `0x50B`, Equilibrado = `0x10B`. **Responde o A16: o app usa `SetGamingMiscSetting`, não `SetGamingProfile`.**
- Logo depois chama `gpu_set_oc_level(n)` com: Silencioso/Eco → 0, Equilibrado → 1, Desempenho → 2, Turbo → 3.
- Lê `HKLM\SOFTWARE\OEM\AcerAgentService\NV_WhisperMode20_Support` (NVIDIA WhisperMode). Efeito ainda não analisado.

**Overclock da GPU — `nvidiaOC_set_oclevel(n)` (AcerHardwareService):**
- Escolhe um par (offset núcleo, offset memória) dos 4 níveis (vindos do `.ini`), multiplica por uma constante (MHz → kHz) e aplica via `NvAPI_GPU_GetPstates20` + **`NvAPI_GPU_SetPstates20`** em cada GPU física, até 5 tentativas.
- É aplicado **em toda troca de modo**: nos modos com offset 0 ele grava 0, ou seja, desfaz o overclock. CONFIRMADO no código.
- Também tem caminho para GPU AMD (`amd_gpu_lib_function`), sem uso neste modelo.

**Fans — `FAN_CONTROL` (AcerAgentService):**
- Máscara de fans: bit 0 = CPU; bit `3 + i` = GPU i (GPU1 = 0x08, GPU2 = 0x10).
- Modo por fan (2 bits): CPU nos bits 0–1; GPU i nos bits `6 + 2i`. **1 = Auto, 2 = Max, 3 = Custom.**
- `SetGamingFanBehavior(gmInput = (modos << 16) | máscara)`.
- **Auto / Max** (`mode` 0 / 1 no app): todas as fans com `modo = mode + 1` numa única chamada → CPU+GPU: Auto `0x00410009`, Max `0x00820009`. **CONFIRMADO no código.**
- **Custom** (`mode` 2): cada fan pode ser Auto ou Custom. O app faz **duas chamadas**: uma com as fans "auto" (modo 1) e outra com as "custom" (modo 3); depois `SetGamingFanSpeed(gmInput = (% << 8) | id)` para cada fan custom, com id 1 = CPU e `4 + i` = GPU i. Sem valor definido, usa 50%.
- Leitura: `GetGamingFanBehavior(máscara)` → modo da CPU nos bits 8–9 da saída e da GPU i nos bits `14 + 2i` (bate com `0x41`/`0x82`/`0xC3` no byte 1). `GetGamingFanSpeed(id)` → % no byte 1.
- **Conclusão para o projeto:** controlar cada fan de forma independente (uma em curva própria, outra no Auto do EC) é suportado pelo próprio protocolo da Acer. CONFIRMADO no código; falta só o teste de escrita (A15).

**Limite de bateria (~80%) — Acer Care Center (`AcerCCAgent.exe`, módulo `i3d::AcerWMI`):**
- Dois caminhos: "AcerHID2025" (EC por HID, usado só quando `EC_HID_Support = 1`; aqui é 0) e **WMI `BatteryControl`** (o deste modelo).
- `SetBatteryHealthControl(uBatteryNo, uFunctionMask, uFunctionStatus, uReservedIn[5])`: o código passa os valores direto (`BatteryIndex`, `BatteryFunctionMask`, `bool` liga/desliga), reservado zerado.
- `GetBatteryHealthControlStatus(uBatteryNo, uFunctionQuery = 1, uReserved[2])`: `uFunctionList` é uma máscara de funções suportadas, lida como **bit0, bit1, bit2**; `uFunctionStatus[0..2] == 1` = função ligada.
- Leitura deste notebook: `list = 03` → bit0 e bit1 suportados; `status = 00 00 FF` → as duas desligadas.
- SUPOSIÇÃO forte: **bit0/máscara 1 = "Battery Boundary" (limite ~80%)**, bit1/máscara 2 = calibração. Então ligar o limite = `SetBatteryHealthControl(1, 1, 1)`, desligar = `(1, 1, 0)`. Confirmar com o teste A8 (ligar no Care Center e ver `status[0]` virar 1).
- O limite via WMI é só liga/desliga (sem escolher o %). O caminho HID 2025 tem `Get/SetBatteryBoundary(bool, min, max)`, mas não se aplica a este modelo.

**Teclado (`AcerECKeyboardController.dll`, modo estático):**
- Cor por zona: `SetGamingRgbKb(gmInput = máscara_zona | R << 8 | G << 16 | B << 24)`, uma chamada por zona (máscaras 1, 2, 4, 8), com 30 ms entre elas. Bate com o ACPI (`KBnR/G/B`).
- Efeito/brilho: `SetGamingKBBacklight(UInt8[])` com `[0]` efeito, `[1]` velocidade, `[2]` **brilho**, `[4]` direção, `[5..7]` RGB, `[8]` `KLES`, `[9]` alvo (1 = teclado, 2 = light bar). No estático o app manda efeito 0, velocidade 0, brilho N, `KLES = 3`, alvo 1, e depois as cores por zona.
- **Liga/desliga por zona**: `SetGamingLED(UInt8[])` com `[0] = 8` e `[5]` = máscara das zonas acesas (bit0 = zona 1 … bit3 = zona 4). Grava `EC.KBST`. Explica as leituras anteriores: `KBST = 7` (zonas 1–3) e `0x0F` (as 4). CONFIRMADO no código + ACPI.
- O tamanho do buffer muda conforme a versão da tabela SMBIOS tipo 172 (8, 12 ou 16 bytes).
- Os números de cada efeito animado (onda, neon…) não estão nesta DLL. Descobrir lendo `KBLE` no teste A9.

**Funções extras do PredatorSense:**
| Função | Mecanismo (CONFIRMADO no código) |
|---|---|
| Sticky key | só Windows: `SystemParametersInfo(SPI_GET/SETSTICKYKEYS)`, liga/desliga as flags 0x5 |
| Trava da tecla Windows | **HID feature report** de 8 bytes para o teclado: `03 xx 00 00 00 00 00 cs`, `xx = 0` (tecla liberada) ou `3` (travada), `cs = 0xFF − soma(bytes 0..6)` |
| Luz do teclado desliga após 30 s | HID feature report: `30 01 xx 00 00 00 00 cs`, `xx = 1` liga, `0` desliga, mesmo checksum |
| Modo de som DTS | DLL do driver de áudio (`DtsApoAppWin32DLL.dll`, `create_DtsApoInterface`). Fora do firmware |
| LCD overdrive | suporte lido de `GetGamingProfile(0)`. O comando de escrita ainda não foi localizado |
| GPU dedicada (MUX) | suporte lido de `GetGamingMiscSetting(9)` (via SMI) e do registro `discrete_gpu_support` / `dgpu_mode_capability` |
| Tecla Turbo | o serviço recebe o evento e reenvia ao app (`kAgtTurboKeyPressed`) |
| Battery Boost | só lido via "EcHID" (não se aplica: `EC_HID_Support = 0`) |
| Logo de boot da BIOS | ⛔ fora do escopo (grava arquivos para a BIOS) |
- VID/PID do teclado HID: vem da configuração do serviço; não extraído. SUPOSIÇÃO: dá para achar pelo Gerenciador de Dispositivos.

### Tabela DTT (`GDDV`) no ACPI — adiada
- `\_SB.IETM` na SSDT; `GDDV` devolve um "OEM Exported DataVault" (cabeçalho `gddv`, bloco `REPO` comprimido em LZMA), com 2 variantes: `PLID = 0x1B` e padrão.
- Decodificar exige descompactar LZMA e interpretar o formato proprietário da Intel. Custo alto; o teste **A18** (log do HWiNFO em cada modo) dá os limites reais com muito menos esforço. Fica como opcional.

### Teste A15 — escritas de fan pela nossa ferramenta (2026-11-05 22:02, Equilibrado, na tomada, repouso)
Ferramenta: `tests/KillerSense.HwTests` (`custom --cpu 40 --gpu 40 --hold 20`), usando o mesmo `AcerWmiFirmware` do projeto. Saída em `recon-out/a15-custom-40-40.txt`.
| Escrita | Valor | Resultado |
|---|---|---|
| `SetGamingFanSpeed` CPU 40% | `0x2801` | status 0; `GetGamingFanSpeed(1)` = 40 |
| `SetGamingFanSpeed` GPU 40% | `0x2804` | status 0; `GetGamingFanSpeed(4)` = 40 |
| `SetGamingFanBehavior` Custom | `0x00C30009` | status 0; modo lido = Custom nas duas |
| `SetGamingFanBehavior` Auto | `0x00410009` | status 0; modo lido = Auto nas duas |

- **CONFIRMADO no hardware:** formato de `SetGamingFanSpeed` (`(% << 8) | id`, CPU = 1, GPU = 4) e de `SetGamingFanBehavior` Custom (`0x00C30009`). Toda a cadeia `AcerWmiFirmware` → WMI → firmware funciona.
- RPM com 40%: subiu em ~2 s e estabilizou em **CPU ~4166 rpm, GPU ~3896 rpm** (Auto em repouso: ~2054 / ~1754). A CPU caiu de 68 para 64 °C em 20 s.
- ⚠️ **A relação % → RPM não é linear e começa alta**: 40% já gira ~60% do máximo (6976). No teste de 10/40 (A10), 10% ainda dava ~3600 rpm. Consequência: **em repouso, o Auto do EC é mais silencioso que qualquer % baixo do Custom**. Os pisos de segurança (20–45%) provavelmente fazem a fan girar mais do que o Auto em repouso. Revisar no A17.
- Logo após voltar ao Auto, o RPM ainda estava alto (a fan desacelera aos poucos) — esperado.
- Antes do teste, o EC guardava CPU 10% / GPU 40% (do teste A10).

### Teste A17 — curva % → RPM (2026-11-05 22:04–22:05, Equilibrado, na tomada, repouso, CPU 59–65 °C)
Ferramenta: `HwTests custom --cpu N --gpu N --hold 15`, uma execução por valor, volta ao Auto entre elas. Saída: `recon-out/a17-curve.txt`.
| % Custom | Fan CPU (rpm) | Fan GPU (rpm) |
|---|---|---|
| Auto do EC, repouso | ~2040 | ~1750 |
| 20 | 3157 | 2857 |
| 30 | 3703 | 3409 |
| 40 | 4166 | 3896 |
| 50 | 4615 | 4411 |
| 60 | 5084 | 4838 |
| Max (A4) | 6976 | 6976 |
- **CONFIRMADO:** entre 20 e 60% a relação é quase linear — CPU ≈ 2200 + 48 × %, GPU ≈ 1870 + 50 × % (rpm). Extrapolando, 0% ≈ 2200 / 1870 rpm: **o Custom nunca fica abaixo de ~2000 rpm**, praticamente o mesmo do Auto em repouso.
- A GPU estabiliza um pouco mais devagar (até ~3 s).
- **Consequência:** os pisos aprovados (20/25/35/45%) equivalem a ~3150–4350 rpm, **bem mais barulhentos que o Auto em repouso (~2040)**. Proposta de ajuste em docs/arquitetura.md §5.4 (aguardando decisão).

### Teste A16 — escrita do modo de operação (2026-11-06 13:46–13:50, na tomada, curva do killer-sense ativa)
Ferramenta: `HwTests mode --set <modo> [--watch S]` (só aceita o subcomando 0x0B; Desempenho/Turbo só na tomada, Eco só na bateria). Saídas em `recon-out/a16-*.txt`.

| Hora | Escrita | `OPMS` lido depois | Observação |
|---|---|---|---|
| 13:46:14 | `0x00B` Silencioso | Quiet por 10 s | o PredatorSense mostrou **Silencioso** (usuário) |
| 13:46:47–51 | — | **Balanced** | o usuário **só abriu a janela** do PredatorSense: ela reaplicou o modo salvo (Equilibrado) e pôs as fans no Auto (o serviço detectou e pausou 30 s) |
| 13:46:57 | `0x40B` Desempenho | Performance | ficou até a próxima escrita (13:49:54) com a janela do PredatorSense fechada, inclusive 60 s acompanhados |
| 13:49:54 | `0x50B` Turbo | Turbo por 30 s | piso da curva subiu para 30% (≈4000 rpm). GPU ficou em **P8** em repouso: o overclock/P0 do Turbo do PredatorSense **não** entra |
| 13:50:39 | `0x10B` Equilibrado | Balanced por 15 s | fan da CPU voltou a ~14% |

- **CONFIRMADO:** `SetGamingMiscSetting((modo << 8) | 0x0B)` troca o modo; status 0 em todas as escritas; o PredatorSense reconhece o modo novo.
- **CONFIRMADO:** os serviços da Acer em segundo plano **não** desfazem o modo. Quem desfaz é a **janela do PredatorSense ao abrir** (reaplica o modo e as fans salvos nela).
- **CONFIRMADO:** o overclock da GPU por modo não vem do firmware; é o serviço da Acer (NVAPI) que aplica quando *ele* troca o modo. Pelo killer-sense, Turbo = só limites de potência do firmware. Overclock fica para a parte de GPU.
- Os valores de PL1/PL2 de cada modo não foram anotados; continuam no A18.
- Achado lateral: com o controle pausado o serviço não chama o firmware e mostra o último modo lido (13:47:19 mostrou "Balanced" já em Performance).

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
| A8 | Limite de bateria 80% no Care Center (liga/desliga) | — | **adiado pelo usuário (2026-11-05)**; o Get de BatteryControl já funciona | campos de `GetBatteryHealthControlStatus` (hoje `list=03`, `status=00 00 FF 00 00`) |
| A9 | Teclado: trocar cor, efeito e brilho | — | **adiado pelo usuário (2026-11-05)** | confirmar `KBBP` = brilho, `KBLE` = efeito. Roteiro: estático com zonas vermelho/verde/azul/branco e brilho 50; depois Onda variando velocidade e direção; depois outros efeitos e luz desligada |
| ~~A10~~ | ~~Custom com valores diferentes~~ | — | **feito 2026-11-05 (10/40)** | `C1FR` = CPU, `G1FR` = GPU |
| ~~A11~~ | ~~`CTML` × HWiNFO~~ | — | **feito 2026-11-05** | `CTML` = temperatura da CPU |
| A18 | O que cada modo faz: PL1/PL2 da CPU, limites de potência e térmico da GPU, clocks e offsets | HWiNFO gravando log em cada modo (Silencioso, Equilibrado, Desempenho, Turbo; Eco na bateria) | não medido | tabela por modo |
| A17 | Curva % → RPM de cada fan (Custom 20/40/60/80/100, modo Equilibrado fixo, em repouso) | HWiNFO aberto | não linear no A10 | tabela % × RPM e tempo de resposta |
| A12 | Tecla de modos: opção da tecla (`FMKY`?) e evento WMI | na tomada | **baixa prioridade** (usuário) | o essencial já foi feito: a tecla só avisa o software |
| ~~A13~~ | ~~GPU OC do Turbo~~ | — | **feito 2026-11-05** | memória +200 confirmado; núcleo +100 sem carga não aparece |
| A14 | Funções extras do PredatorSense: desligar backlight após 30 s, trava da tecla Windows, sticky key, LCD overdrive, modo de som DTS, USB charging desligado (Quick Access) | — | mapear se entram na paridade | qual método/campo muda em cada uma |
| ~~A15~~ | ~~Escritas de fan Custom e FanSpeed~~ | — | **feito 2026-11-05** (Max não testado: o usuário não quer fan a 100%) | Custom 40/40 → ~4166 / ~3896 rpm |
| ~~A16~~ | ~~Escrita de modo: `SetGamingMiscSetting((modo << 8) \| 0x0B)`~~ | — | **feito 2026-11-06** | `OPMS` muda e fica; abrir a janela do PredatorSense reaplica o modo salvo nela |

**Objetivo do mapeamento (definido pelo usuário em 2026-11-05):** paridade com todas as funções do PredatorSense antes da Fase 2. Recursos fora do PredatorSense (monitoramento extra etc.) ficam para depois. Fora do escopo, por risco: logo de boot da BIOS e tudo que use as classes ⛔.

## Pendente
- `GGSI` (SysInfo): índices 4,5,7,8,11–17 vazios.
- Formato de entrada de `SetGamingFanBehavior` e `SetGamingProfile` (SMI): passo 5 (captura).
- Tudo marcado "CONFIRMADO (ACPI)" precisa de uma leitura real (Get*, admin) para virar CONFIRMADO.
