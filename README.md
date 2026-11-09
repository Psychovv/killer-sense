<p align="center">
  <img src="docs/marca/icone.png" width="112" alt="killer-sense icon">
</p>

<h1 align="center">killer-sense</h1>

<p align="center">
  Fan, mode, battery, keyboard lighting and GPU control for the <b>Acer Predator Helios Neo 16 (PHN16-72)</b>.<br>
  A lightweight alternative to PredatorSense, built for Windows 11.
</p>

> [!WARNING]
> **Unofficial** project, with no affiliation with Acer. It writes to the notebook's firmware to control the fans and the keyboard lighting. It was only built and tested on the PHN16-72. On any other model, do not use it before checking the interface (see [docs/acer-interface.md](docs/acer-interface.md)). Use at your own risk.

> [!NOTE]
> The app's interface and the files in `docs/` are in Brazilian Portuguese, so the page and mode names below are kept as they appear in the app.

## What it does

| Page | What's in it |
|---|---|
| **Painel** | CPU and GPU temperature, fan, rotation speed, usage, clocks and power; RAM in use; the heaviest programs; a chart of the last 2 minutes (temperature or usage). |
| **Fans** | One curve per fan, with 2 to 10 points (double-click adds, drag moves, right-click removes), with hysteresis and ramping. Below 60 °C on the CPU and 55 °C on the GPU the fan stays on the notebook's own Auto, which is the quietest. |
| **Modos** | Silencioso, Equilibrado, Desempenho, Turbo and Eco (on battery), with each mode's CPU power limit and live numbers. |
| **Perfis** | Mode + fans + GPU offset + battery + lighting in one click, from the page or from the tray. Export and import. A "Padrão Acer" profile goes back to the factory settings. |
| **Bateria** | Charge limit at 80% (the same as Acer Care Center), health and capacity. |
| **Iluminação** | 4-zone keyboard: color per zone, Acer's 8 effects, brightness, speed and direction. Off by default; when on, killer-sense rewrites the lighting whenever the Acer service overwrites it (for example, after resuming from standby). |
| **GPU** | Core and memory clock offset per mode (off by default). The GPU is only read when it is already awake, so it is never pulled out of its power-saving state (Optimus). |
| **Configurações** | Start with Windows (tray only), °C or °F, theme, accent color and log folder. |

In the tray: switch profile, go back to Auto, open the window and quit. Quitting hands the fans back to the notebook's Auto.

## Safety

The notebook keeps the final say: the **EC's Auto** (the factory control) is the safe state, and killer-sense goes back to it whenever something unexpected happens.

- The service **always starts on Auto** and goes back to it when stopping, when Windows shuts down, or on an error.
- **Watchdog**: a second, minimal service. If the main service dies while a fan is on manual, it hands Auto back in about 3 s (measured: 2.6 s). Windows then restarts the service.
- **Emergency**: CPU at 95 °C or GPU at 87 °C ⇒ fans at maximum until it drops by 10 °C.
- **Invalid readings** (3 in a row) from a sensor used in a curve ⇒ that fan goes back to Auto.
- **Doesn't fight PredatorSense**: if another program changes the fans, killer-sense pauses and tries again after 30 s. If that happens more than 3 times in 10 minutes, it stays paused.
- **Allowlist**: only the firmware methods already mapped can be called. The BIOS classes are blocked in the code.
- The window runs **without administrator privileges**. The service re-checks every request that comes from it.

## Lightweight

Measured on the notebook on 2026-11-07, with both installed:

| | killer-sense | PredatorSense 5.0 |
|---|---|---|
| RAM with the window closed | ~140 MB (service, watchdog and tray) | 407 MB for the app alone (all of Acer's software: ~1.08 GB) |
| RAM with the Painel open | ~90 MB for the window | — |
| Disk | 119 MB | 491 MB for the app alone |

No Electron and no embedded browser: C# with .NET 10 and Avalonia. The window renders on the CPU, because rendering on the GPU cost ~100 MB more, almost all of it from the video driver. The Painel only measures usage, RAM and programs while it is visible.

## How it works

```
 KillerSense.App (user)             named pipe            KillerSense.Service (LocalSystem)
 window + tray, Avalonia     ◄──────────────────────►  1 s loop: sensors, curves, failsafe
                                 SYSTEM and the user only       │
                                                                ├─ Acer WMI (fans, modes, battery, lighting)
 KillerSense.Watchdog (LocalSystem)                             ├─ NVML / NVAPI (GPU)
 if the service stops sending a signal ⇒ EC Auto                └─ heartbeat every 1 s ──► Watchdog
```

| Project | Role |
|---|---|
| `KillerSense.Firmware` | The only one that talks to Acer's WMI: typed calls, allowlist, validated ranges and a read-back of every write. |
| `KillerSense.Core` | Curves, failsafe, modes, profiles, lighting. No hardware access, which is why the tests cover it. |
| `KillerSense.Service` | Windows service: control loop, telemetry and the pipe server. |
| `KillerSense.Watchdog` | Minimal service that only knows how to hand Auto back. |
| `KillerSense.Gpu` | NVML (temperature, clocks, power) and NVAPI (clock offset), calling NVIDIA's DLLs directly. |
| `KillerSense.Ipc` | JSON messages between the window and the service. |
| `KillerSense.App` | Window and tray (Avalonia 12, MVVM, with trimming). |
| `KillerSense.Tests` | 240 unit tests with simulated firmware. |
| `KillerSense.HwTests` | Scripts to test on the real notebook, one step at a time. |

## Install

Requires Windows 11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build. The publish is self-contained: the notebook doesn't need .NET installed.

```powershell
# 1. Publish (no administrator needed). Produces the publish\ folder
.\scripts\install\publish.ps1

# 2. Install, in a PowerShell running as administrator
.\scripts\install\install-services.ps1 -PublishDir .\publish
.\scripts\install\install-app.ps1 -PublishDir .\publish
```

The services go in `C:\Program Files\KillerSense\Services` and the app in `C:\Program Files\KillerSense\App`, with a "killer-sense" shortcut in the Start menu. Configuration and logs go in `%ProgramData%\KillerSense`.

- **Update**: close the app (tray ⇒ Quit), publish again and run `update-services.ps1` and `install-app.ps1` with the same `-PublishDir`.
- **Remove**: `uninstall-services.ps1` stops the services (the fans go back to Auto) and deletes the folders and the shortcut.
- If PowerShell refuses the script, run it with `powershell -ExecutionPolicy Bypass -File <script> ...`.

Acer's software can stay installed. Just don't leave PredatorSense open changing the fans at the same time; if it does, killer-sense notices and pauses.

## Developing without touching the hardware

The service and the app have a simulated mode that makes no calls to the firmware or the GPU driver, and doesn't need administrator privileges:

```powershell
dotnet build KillerSense.slnx
dotnet test tests/KillerSense.Tests

# in two terminals:
dotnet run --project src/KillerSense.Service -- --simulated --sim-gpu
dotnet run --project src/KillerSense.App -- --simulated
```

`--sim-gpu` simulates a powered-on GPU (without it, the GPU shows up as "asleep"). The simulated mode uses its own pipe, fan configuration, profiles and log, so it can run alongside the installed version.

## Documentation

The documents are in Brazilian Portuguese.

- [docs/acer-interface.md](docs/acer-interface.md): the map of Acer's firmware interface on this notebook (WMI, ACPI, EC), with what was **confirmed** on the hardware and what is still an assumption.
- [docs/arquitetura.md](docs/arquitetura.md): decisions, safety, telemetry and interface.
- [docs/testes-hardware.md](docs/testes-hardware.md): every test done on the real notebook, with date and result.

## Status

Everything above works and was tested on the PHN16-72, with these exceptions:

- **Profiles**: tested in simulated mode; the test on the notebook is still missing.
- **Emergency**: tested only with simulated firmware (it would need the CPU at a real 95 °C).
- **CPU power (PL1/PL2)**: left out for now, because it needs a low-level driver.
- **Other models**: not tested.

## Legal notice

This repository contains no code, binaries, files or images from Acer. The interface was mapped on this notebook, from its own ACPI tables and from tests, one step at a time. The firmware dumps are kept out of git. Acer, Predator and PredatorSense are trademarks of Acer Inc.
