<p align="center">
  <img src="docs/marca/icone.png" width="112" alt="Ícone do killer-sense">
</p>

<h1 align="center">killer-sense</h1>

<p align="center">
  Controle de fans, modos, bateria, luz do teclado e GPU para o <b>Acer Predator Helios Neo 16 (PHN16-72)</b>.<br>
  Uma alternativa leve ao PredatorSense, feita para Windows 11.
</p>

> [!WARNING]
> Projeto **não oficial**, sem nenhuma ligação com a Acer. Ele grava no firmware do notebook para controlar as fans e a luz do teclado. Só foi feito e testado no PHN16-72. Em outro modelo, não use sem antes conferir a interface (veja [docs/acer-interface.md](docs/acer-interface.md)). Use por sua conta.

## O que faz

| Página | O que tem |
|---|---|
| **Painel** | Temperatura, fan, rotação, uso, clock e potência da CPU e da GPU; RAM em uso; os programas que mais pesam; gráfico dos últimos 2 minutos (temperatura ou uso). |
| **Fans** | Uma curva por fan, de 2 a 10 pontos (duplo clique adiciona, arrastar move, clique direito remove), com histerese e rampa. Abaixo de 60 °C na CPU e 55 °C na GPU a fan fica no Auto do próprio notebook, que é o mais silencioso. |
| **Modos** | Silencioso, Equilibrado, Desempenho, Turbo e Eco (na bateria), com o limite de potência da CPU de cada um e os números ao vivo. |
| **Perfis** | Modo + fans + offset da GPU + bateria + luz num clique, pela página ou pela bandeja. Exportar e importar. Perfil "Padrão Acer" para voltar ao de fábrica. |
| **Bateria** | Limite de carga em 80% (o mesmo do Acer Care Center), saúde e capacidade. |
| **Iluminação** | Teclado de 4 zonas: cor por zona, os 8 efeitos da Acer, brilho, velocidade e direção. Vem desligado; ligado, o killer-sense regrava a luz quando o serviço da Acer troca por cima (por exemplo, ao voltar do standby). |
| **GPU** | Offset de clock do núcleo e da memória por modo (desligado por padrão). A GPU só é lida quando já está acordada, para não tirá-la do modo de economia (Optimus). |
| **Configurações** | Iniciar com o Windows (só na bandeja), °C ou °F, tema, cor de destaque e pasta de logs. |

Na bandeja: trocar de perfil, voltar ao Auto, abrir a janela e encerrar. Encerrar devolve as fans ao Auto do notebook.

## Segurança

O notebook continua sendo o dono da última palavra: o **Auto do EC** (o controle de fábrica) é o estado seguro, e o killer-sense volta a ele sempre que algo sai do esperado.

- O serviço **sempre começa no Auto** e volta a ele ao parar, ao desligar o Windows ou num erro.
- **Watchdog**: um segundo serviço, mínimo. Se o serviço principal morrer com uma fan em manual, ele devolve o Auto em cerca de 3 s (medido: 2,6 s). Depois o Windows reinicia o serviço.
- **Emergência**: CPU a 95 °C ou GPU a 87 °C ⇒ fans no máximo até cair 10 °C.
- **Leituras inválidas** (3 seguidas) de um sensor usado numa curva ⇒ aquela fan volta ao Auto.
- **Não briga com o PredatorSense**: se outro programa muda as fans, o killer-sense pausa e tenta de novo depois de 30 s. Se isso acontecer mais de 3 vezes em 10 minutos, fica pausado.
- **Lista de permissão**: só os métodos de firmware já mapeados podem ser chamados. As classes de BIOS ficam bloqueadas no código.
- A janela roda **sem privilégio de administrador**. O serviço confere de novo cada pedido que chega dela.

## Leve

Medido no notebook em 2026-11-07, com os dois instalados:

| | killer-sense | PredatorSense 5.0 |
|---|---|---|
| RAM com a janela fechada | ~140 MB (serviço, watchdog e bandeja) | 407 MB só o app (o software da Acer todo: ~1,08 GB) |
| RAM com o Painel aberto | ~90 MB a janela | — |
| Disco | 119 MB | 491 MB só o app |

Sem Electron e sem navegador embutido: C# com .NET 10 e Avalonia. A janela desenha pela CPU, porque desenhar pela GPU custava ~100 MB a mais, quase tudo do driver de vídeo. O Painel só mede uso, RAM e programas enquanto está visível.

## Como funciona

```
 KillerSense.App (usuário)          pipe nomeado          KillerSense.Service (LocalSystem)
 janela + bandeja, Avalonia  ◄──────────────────────►  laço de 1 s: sensores, curvas, failsafe
                                 só SYSTEM e o usuário          │
                                                                ├─ WMI da Acer (fans, modos, bateria, luz)
 KillerSense.Watchdog (LocalSystem)                             ├─ NVML / NVAPI (GPU)
 se o serviço parar de dar sinal ⇒ Auto do EC                   └─ batimento a cada 1 s ──► Watchdog
```

| Projeto | Papel |
|---|---|
| `KillerSense.Firmware` | O único que fala com o WMI da Acer: chamadas tipadas, lista de permissão, faixas validadas e leitura de volta de toda escrita. |
| `KillerSense.Core` | Curvas, failsafe, modos, perfis, luz. Sem acesso a hardware, por isso é o que os testes cobrem. |
| `KillerSense.Service` | Serviço do Windows: laço de controle, telemetria e o servidor do pipe. |
| `KillerSense.Watchdog` | Serviço mínimo que só sabe devolver o Auto. |
| `KillerSense.Gpu` | NVML (temperatura, clocks, potência) e NVAPI (offset de clock), chamando as DLLs da NVIDIA direto. |
| `KillerSense.Ipc` | Mensagens JSON entre a janela e o serviço. |
| `KillerSense.App` | Janela e bandeja (Avalonia 12, MVVM, com trimming). |
| `KillerSense.Tests` | 240 testes de unidade com firmware simulado. |
| `KillerSense.HwTests` | Roteiros para testar no notebook real, um passo por vez. |

## Instalar

Precisa de Windows 11 x64 e do [SDK do .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) para compilar. A publicação é autocontida: no notebook não precisa ter o .NET instalado.

```powershell
# 1. Publicar (sem administrador). Gera a pasta publish\
.\scripts\install\publish.ps1

# 2. Instalar, num PowerShell como administrador
.\scripts\install\install-services.ps1 -PublishDir .\publish
.\scripts\install\install-app.ps1 -PublishDir .\publish
```

Os serviços ficam em `C:\Program Files\KillerSense\Services` e o app em `C:\Program Files\KillerSense\App`, com o atalho "killer-sense" no menu Iniciar. Configuração e logs ficam em `%ProgramData%\KillerSense`.

- **Atualizar**: feche o app (bandeja ⇒ Encerrar), publique de novo e rode `update-services.ps1` e `install-app.ps1` com o mesmo `-PublishDir`.
- **Remover**: `uninstall-services.ps1` para os serviços (as fans voltam ao Auto) e apaga as pastas e o atalho.
- Se o PowerShell recusar o script, rode com `powershell -ExecutionPolicy Bypass -File <script> ...`.

O software da Acer pode continuar instalado. Só não deixe o PredatorSense aberto mexendo nas fans ao mesmo tempo; se ele mexer, o killer-sense percebe e pausa.

## Desenvolver sem mexer no hardware

O serviço e o app têm um modo simulado que não faz nenhuma chamada ao firmware nem ao driver da GPU, e não precisa de administrador:

```powershell
dotnet build KillerSense.slnx
dotnet test tests/KillerSense.Tests

# em dois terminais:
dotnet run --project src/KillerSense.Service -- --simulated --sim-gpu
dotnet run --project src/KillerSense.App -- --simulated
```

`--sim-gpu` simula a GPU ligada (sem ele, ela aparece "dormindo"). O simulado usa outro pipe, outra configuração de fans, outros perfis e outro log, então pode rodar ao lado da versão instalada.

## Documentação

- [docs/acer-interface.md](docs/acer-interface.md): o mapa da interface de firmware da Acer neste notebook (WMI, ACPI, EC), com o que foi **confirmado** no hardware e o que ainda é suposição.
- [docs/arquitetura.md](docs/arquitetura.md): decisões, segurança, telemetria e interface.
- [docs/testes-hardware.md](docs/testes-hardware.md): cada teste feito no notebook real, com data e resultado.

## Estado

Tudo acima funciona e foi testado no PHN16-72, com estas exceções:

- **Perfis**: testados no modo simulado; falta o teste no notebook.
- **Emergência**: testada só com firmware simulado (precisaria da CPU a 95 °C de verdade).
- **Potência da CPU (PL1/PL2)**: fora por enquanto, porque exige um driver de baixo nível.
- **Outros modelos**: não testados.

## Aviso legal

Este repositório não contém código, binários, arquivos nem imagens da Acer. A interface foi mapeada neste notebook, a partir das tabelas ACPI dele e de testes, um passo por vez. Os dumps de firmware ficam fora do git. Acer, Predator e PredatorSense são marcas da Acer Inc.
