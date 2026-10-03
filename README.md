# WslcGui

A Docker-Desktop-style Windows GUI for **WSLC** (WSL Containers). It is built with [MewUI](https://github.com/aprillz/MewUI) and published with NativeAOT.

See [PLAN.md](PLAN.md) for the research, architecture, feature list and milestones.

## Features

- **Containers:**
  - A list with status, CPU, memory, ports and age, plus search.
  - Start, stop, restart, kill and remove, including bulk actions on a multi-selection; removing all stopped containers.
  - A **Run** dialog for image, name, command, ports, environment, volumes and auto-remove, with the equivalent `wslc` command.
  - Container details:
    - **Logs:** follows live; filter, timestamps, pause, copy.
    - **Stats:** CPU, memory, network and disk charts.
    - **Files:** browse, preview, download, upload (drag and drop from Explorer works). Stopped containers are shown as a read-only snapshot.
    - **Inspect**.
    - **Exec:** one-off commands.
    - **Open terminal:** `wslc exec -it` in Windows Terminal.
- **Images:**
  - Pull, **build** from a Dockerfile (live output, build arguments) and run.
  - Remove and clean up.
  - Save to, load from and import a tar file; push; registry sign-in. The password goes to `wslc` on stdin, never on the command line.
- **Volumes:** list with "used by", create, remove, clean up, inspect.
- **Networks:** list, create, remove, clean up, inspect.
- **Troubleshoot:** engine details, a log of every `wslc` command the app ran (with timings and errors), copy diagnostics, open the WSLC settings file, restart the engine.
- **Settings:** light, dark or system theme; keep running in the notification area when the window is closed.
- **Tray icon:** a menu showing what's running, and Quit.

The app never keeps the WSLC VM awake:
- Background polling slows to every 45 s when nothing is running, and stops while the VM is idle.
- Event and stats streams run only while containers are running.

## Requirements

- Windows 11 with WSL 3.0+ (`wslc.exe` in `C:\Program Files\WSL`)
- .NET SDK 10.0.4xx (pinned in `global.json`)
- To publish with NativeAOT: the Visual Studio C++ build tools (the "Desktop development with C++" workload)

## Build, test, run

```powershell
dotnet test --solution WslcGui.slnx
dotnet run --project src/WslcGui.App
./build/publish.ps1                 # NativeAOT → artifacts/publish/win-x64/WslcGui.exe
```

## Layout

| Project | Role |
|---|---|
| `src/WslcGui.Engine.Abstractions` | Engine interfaces and plain models. No implementation. |
| `src/WslcGui.Engine.Cli` | The engine implemented over `wslc.exe`. **The only project allowed to start processes.** |
| `src/WslcGui.Core` | UI-agnostic view models. Depends on the abstractions only. |
| `src/WslcGui.App` | The MewUI app. `Program.cs` is the composition root. |
| `tests/*` | xUnit v3 tests. `tests/fixtures/cli` holds captured `wslc` output. |

A banned-API analyzer (`BannedSymbols.txt`) turns any use of `System.Diagnostics.Process` outside `WslcGui.Engine.Cli` into a build error.

## Regenerating icons

`src/WslcGui.App/Icons/IconData.g.cs` holds Fluent UI System Icons (MIT), taken from MewUI's Gallery. To add an icon, add its name to `tools/extract-icons.py` and run `python tools/extract-icons.py`. This needs `ref/MewUI` cloned. The app icon (`src/WslcGui.App/Assets/app.ico`) is drawn by `python tools/make-icon.py`, which needs Pillow.
