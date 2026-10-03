# WslcGui

A Docker-Desktop-style Windows GUI for **WSLC** (WSL Containers). It is built with [MewUI](https://github.com/aprillz/MewUI) and published with NativeAOT.

See [PLAN.md](PLAN.md) for the research, architecture, feature list and milestones.

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

`src/WslcGui.App/Icons/IconData.g.cs` holds Fluent UI System Icons (MIT), taken from MewUI's Gallery. To add an icon, add its name to `tools/extract-icons.py` and run `python tools/extract-icons.py`. This needs `ref/MewUI` cloned.
