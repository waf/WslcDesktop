# WSLC Desktop

A Desktop Windows GUI for **WSLC** (WSL Containers).

It aims to be fast and resource efficient; it's one self-contained `WslcDesktop.exe`
of about 10 MB with memory usage around 90 MB. It does not need the .NET runtime;
it's built with [MewUI](https://github.com/aprillz/MewUI) and published with NativeAOT.

[![WSLC Desktop screenshot](assets/screenshot.png)](assets/screenshot.png)

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

## Development Requirements

- Windows 11 with WSL 3.0+ (`wslc.exe` in `C:\Program Files\WSL`)
- .NET SDK 10.0.4xx (pinned in `global.json`)
- To publish with NativeAOT: the Visual Studio C++ build tools (the "Desktop development with C++" workload)

## Build, test, run

```powershell
dotnet test --solution WslcDesktop.slnx
dotnet run --project src/WslcDesktop.App
./build/publish.ps1                 # NativeAOT → artifacts/publish/win-x64/WslcDesktop.exe
```

A banned-API analyzer (`BannedSymbols.txt`) turns any use of `System.Diagnostics.Process` outside `WslcDesktop.Engine.Cli` into a build error.  In the future we may investigate using COM calls instead of invoking `wslc.exe` to avoid the overhead of starting a new process; but the COM API is not a stable API. Restricting the use of `Process` to a single project makes it easier to switch to COM later.
