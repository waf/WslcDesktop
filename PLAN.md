# WslcGui: implementation plan

This is a Docker-Desktop-style Windows GUI for **WSLC** (WSL Containers). It is built with **MewUI** and published with **NativeAOT**.

Status: research done (2026-10-03). Nothing is implemented yet.

---

## 1. Research findings that shape the design

### 1.1 The WSLC SDK is too small to build this app on its own

The SDK is NuGet package `Microsoft.WSL.Containers` 3.0.1. It is a CsWinRT projection over native `wslcsdk.dll`. It is built for **apps that embed containers**, not for managing a container engine.

What the SDK does **not** have:

| Gap | Effect on the GUI |
|---|---|
| No container list | It can only `OpenContainer(nameOrId)` |
| No logs API | It only sees live output, and only if attached before `Start()` |
| No stats, no events stream | No resource graphs; no live refresh |
| No TTY/PTY, no resize | Exec gives plain pipes only |
| No file copy or browse, no image inspect/build/save | Those pages can't be built on it |
| No volume or network listing | Those pages can't be built on it |
| Pulls cannot be cancelled | Cancel button won't work |
| **No way to open or list existing sessions** | The SDK **cannot reach the CLI's default session**. The service reserves `wslc-cli-*` names. |
| **SDK sessions are non-persistent** | The session's VM (and its running containers) goes away when the GUI process exits |

The **`wslc.exe` CLI has all of it**:
- `container list/inspect/logs -f/stats/exec -it/attach/cp/export/prune/restart`
- `image build/inspect/save/load/import/push/prune/tag`
- `network *` and `volume *`
- `events`, `info`, `version`, `system session list`, `settings`, `login/logout`

`--format json` is available on `list`, `images`, `stats`, `info` and `version`. `inspect` returns JSON by default.

The internal service COM API (`wslc.idl`) has everything too. It is undocumented, unversioned and likely to change, so we avoid it for now (see 1.1a).

### 1.1a Process overhead, and calling the CLI's code directly

- **Measured cost of a `wslc` call:** spawning the process, activating COM and calling the service takes about 20–26 ms per call on this machine.
  - `wslc version` averaged 21 ms; `wslc system session list` averaged 26 ms.
  - At a 3–5 s polling interval this is negligible.
  - Long-running streams (`logs -f`, `events`) cost one process each, started once.
- **The CLI has no library to call.** `wslc.exe` statically links an internal object library (`wslclib`) and exports nothing. `ContainerListCommand.cpp` is only argument and output glue:
  - `ResolveSession` → `GetContainers` → `FormatContainerOutput`.
  - `ContainerService::List` does `CoCreateInstance(CLSID_WSLCSessionManager {a9b7a1b9-0671-405c-95f1-e0612cb4ce8f}, CLSCTX_LOCAL_SERVER)`, then `IWSLCSessionManager::OpenSessionByName(null)` (null means the default session), then `IWSLCSession::ListContainers(...)`.
- **We could make the same COM calls from C#.**
  - `IWSLCSessionManager` {82A7ABC8-…} has its proxy/stub registered in HKLM, and the CLSID is registered.
  - .NET's `[GeneratedComInterface]` source generator is NativeAOT-safe.
  - Benefits:
    - sub-millisecond, typed calls;
    - callbacks for pull and build progress;
    - an events stream;
    - **a real TTY exec with `ResizeTty`, without ConPTY or `wslc.exe`**.
  - Costs:
    - We hand-translate the IDL structs: fixed-size char arrays, CoTaskMem-allocated out arrays.
    - We replicate `ConfigureForCOMImpersonation`, which is `CoSetProxyBlanket`.
    - **No compatibility promise.** `WSLCCompat.idl` says "changes must maintain backwards compatibility"; `wslc.idl` says nothing of the kind. Interface IIDs might not change when struct layouts do, so a WSL update could cause silent memory corruption rather than a clean error.
    - To use it, gate on `IWSLCSessionManager::GetVersion` against an allow-list of tested WSL versions, and fall back to the CLI for anything else.
- **Decision:** start with the CLI engine. Spike S6 decides whether to add a `ComEngine` for hot paths and TTY.

**Recommendation: the main backend drives `wslc.exe`** (spawn the process, parse JSON), against the CLI's default per-user session.
- The GUI sees the same containers as the user's terminal, which is what Docker Desktop users expect.
- Containers are not tied to the GUI's lifetime.
- All data-access code sits behind an `IContainerEngine` interface. That leaves room for an SDK-backed engine, which suits app-owned sessions or typed pull progress, and for a future official management API.

### 1.2 MewUI fits the job

- **Version and packages:** v0.22.1 on NuGet. Use `Aprillz.MewUI.Core` + `.Platform.Win32` + `.Backend.Direct2D`, plus optional `.Svg`, `.MewDock` and `.MewCharts`.
- **NativeAOT check:** a test app built with NativeAOT gave **0 trim/AOT warnings**, a 4.4 MB exe and about 65 MB working set. It contained bindings, background work marshalled via `Dispatcher.BeginInvoke`, a `MultiLineTextBox`, and a custom monospace grid control drawn with paint spans.
- **What we get:**
  - Code-only fluent UI. `MewProperty`/`ObservableValue<T>`/INPC bindings without reflection (a source generator handles binding paths).
  - Commands with key gestures.
  - Theming (Light/Dark/System).
  - Controls: `NavigationView`, `GridView` (typed columns, sorting, virtualization, tree-grid mode), lazy-loading virtualized `TreeView`, `TabControl`, `SplitPanel`, `ContextMenu`, `MessageBox`/dialogs, `ProgressBar`, in-window toasts, `BusyIndicatorService`.
  - Read-only `MultiLineTextBox` with `AppendText` (log view).
  - Extensions: MewDock (closable/dockable tabs), MewCharts (stats graphs), Svg, MewvalonEdit (AvalonEdit-style editor, source only, not on NuGet).
  - Child-HWND hosting, with a WebView2 extension and a WinFormsHost sample as templates.
- **Template to copy:** `samples/MewUI.TaskManager.Sample`. Its NavigationView, GridView with sorting and context menus, and background sampler that marshals to the dispatcher make it almost exactly the right architecture.
- **Gaps we must fill:**
  - No terminal or VT control.
  - No tray icon. Use `Shell_NotifyIcon` via `Window.NativeMessage`.
  - No OS notifications.
  - No status bar. Compose one from a DockPanel.
  - No accessibility/UIA.
  - No closable tabs in core. Use MewDock.
- **Threading:** controls do no cross-thread marshalling, so marshal everything yourself.
- **The API is pre-1.0.** Pin the version.
- **Build notes:**
  - Pin the SDK to 10.0.x with `global.json`. SDK 11 RC is also installed and gets picked up otherwise.
  - The AOT link step needs `vswhere.exe` on PATH when building from Git Bash. Add `C:\Program Files (x86)\Microsoft Visual Studio\Installer`.
  - In the repo clone, `MewUI.Sample` doesn't build for a single RID, and submodules aren't initialized. Use the NuGet packages, not source.

### 1.3 Terminal: we can embed one without writing an emulator

The best choice is **`Microsoft.Terminal.Control.dll`**, the native engine from Windows Terminal (the one Visual Studio uses):
- It has a flat C API: `CreateTerminal(HWND parent, …)`, `TerminalSendOutput`, `TerminalRegisterWriteCallback`, `TerminalSendKeyEvent`/`CharEvent`, `TerminalTriggerResize`, `TerminalSetTheme`, `TerminalSetFocused`, …
- It is hosted as a child HWND and imports only in-box DLLs.
- It is a good fit for `[LibraryImport]` plus `UnmanagedCallersOnly` callbacks under NativeAOT.
- The only distribution is an **unofficial NuGet repackage** (`CI.Microsoft.Terminal.Wpf`). The source is MIT. The API is internal, with no stability promise.

The process side is **ConPTY** (`CreatePseudoConsole`) running `wslc exec -it <id> sh`. That gives a real TTY with resize, which the SDK can't.

Fallbacks:
- xterm.js inside MewUI's WebView2 extension: heavier, but a mature emulator.
- A custom MewUI cell-grid control plus a VT parser: the most work.
- The cheapest option: launch `wslc exec -it` in Windows Terminal.

---

## 2. Architecture

```
WslcGui.slnx
global.json                         -> pin SDK 10.0.x
Directory.Build.props               -> net10.0-windows, Nullable, TreatWarningsAsErrors for IL/AOT warnings
src/
  WslcGui.App/                      MewUI exe (PublishAot, win-x64 + win-arm64)
    Program.cs                      Win32Platform + Direct2DBackend registration, single-instance guard
    Shell/                          MainWindow, NavigationView, status bar, tray icon, toasts
    Pages/                          Containers, Images, Volumes, Networks, Builds, Settings, Troubleshoot
    ContainerDetail/                Logs, Inspect, Files, Terminal, Stats tabs
    Dialogs/                        RunContainerDialog, PullImageDialog, ConfirmDialogs
    Controls/                       LogView, JsonView, StatusBadge, PortLink, icons (Fluent path data)
  WslcGui.Engine.Abstractions/      Engine interfaces + plain models/records ONLY. No implementation, no dependencies.
  WslcGui.Core/                     UI-agnostic view models, polling/event services. Depends on Abstractions only.
  WslcGui.Engine.Cli/               The ONLY project that starts processes: WslcCli runner, JSON source-gen contexts,
                                    Cli* implementations of the engine interfaces, ConPTY terminal session source
  WslcGui.Engine.Com/               (optional, after S6) direct COM implementations of selected interfaces
  WslcGui.Terminal/                 ITerminalView implementations (Terminal.Control interop / fallback). No process code.
tests/
  WslcGui.Core.Tests/               JSON fixture parsing, ANSI parser, ls/stat parsing, arg building
ref/MewUI/                          reference clone (not built)
```

Key design points:

### 2.1 Engine boundary (decided: CLI first, swappable later)

**Rule:** app and UI code talk to engine **interfaces** in `WslcGui.Engine.Abstractions`. They never see `wslc`, process arguments, JSON or exit codes.

**Enforcement:**
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` with a `BannedSymbols.txt` bans `System.Diagnostics.Process`, `ProcessStartInfo` and `CreatePseudoConsole` in every project except `WslcGui.Engine.Cli`.
- `WslcGui.App` and `WslcGui.Core` don't reference `Engine.Cli` types except in a single composition root (`Program.cs`).

**The interfaces are split by capability**, so each one can get its own implementation later (for example `ContainerQueries`/events/terminal over COM, while the rest stays on the CLI). The aggregate is `IContainerEngine`. All methods are async with a `CancellationToken`.

| Interface | Members | Likely future swap |
|---|---|---|
| `IEngineInfo` | `GetInfoAsync`, `GetVersionAsync`, `CheckHealthAsync` | – |
| `IContainerQueries` | `ListAsync(all)`, `InspectAsync(id)` | COM (fast list) |
| `IContainerLifecycle` | `Create/Run/Start/Stop/Restart/Kill/Remove/PruneAsync` | – |
| `IImageService` | `List/Inspect/Remove/Tag/Prune`, `PullAsync(ref, IProgress<PullProgress>)`, `Build/Save/Load/ImportAsync`, `PushAsync` | COM (progress callbacks) |
| `IVolumeService` / `INetworkService` | `List/Inspect/Create/Remove/Prune` (+ `Connect/Disconnect`) | – |
| `IStatsSource` | `SnapshotAsync(ids)` | COM |
| `IEventSource` | `IAsyncEnumerable<EngineEvent> WatchAsync(ct)` | COM |
| `ILogSource` | `IAsyncEnumerable<LogLine> ReadAsync(id, LogOptions, ct)` | COM |
| `IContainerFiles` | `ListDirectoryAsync`, `ReadFileAsync`, `CopyFrom/CopyToAsync`, `ExportAsync` | – |
| `IExecService` | `ExecAsync(id, argv)` returning `ExecResult` (one-shot) | – |
| `ITerminalSessionFactory` | `OpenAsync(id, shell, size)` returning `ITerminalSession` (`Stream Input/Output`, `Resize(cols, rows)`, `Exited`) | COM TTY (removes ConPTY) |
| `IExternalTerminalLauncher` | `Launch(id, shell)` | – |

Notes on the design:
- Models are plain records owned by Abstractions (`ContainerSummary`, `ImageSummary`, `PullProgress`, `EngineEvent`, …).
- CLI DTOs are internal to `Engine.Cli` and mapped to those records, so JSON schema churn stays inside the engine.
- Errors are a single `EngineException` (`Kind`: NotFound / Conflict / NotRunning / Unavailable / Unknown, plus a message). Engines map their own failures into it, from CLI stderr or COM HRESULTs.
- The terminal splits into two halves:
  - `ITerminalSession` is the **byte pipe** (engine side). The CLI implementation is ConPTY + `wslc exec -it`.
  - `ITerminalView` is the **renderer** (UI side).
  - Neither knows about the other's implementation.

### 2.2 Inside `WslcGui.Engine.Cli`

- **`WslcCli`** is the single choke point. It is the only class that touches `Process`.
  - `RunAsync(args) → CliResult`.
  - `RunJsonAsync<T>(args, JsonTypeInfo<T>)`.
  - `StreamLinesAsync(args) → IAsyncEnumerable<string>` for `logs -f`, `events` and build output.
  - `StartPty(args, size)` (ConPTY).
- How it runs `wslc`:
  - Arguments are passed as an *argument list* (no string concatenation, so no quoting bugs), with UTF-8 output.
  - It takes a global `--session` option (default: none, which means the CLI default session).
  - It puts every spawned child in a **Job Object** (kill-on-close), so long-running children die with the app.
- **Hooks:**
  - **Logging:** each call records its arguments, duration and exit code to a debug log, which also feeds the Troubleshoot page.
  - **Concurrency limit:** a semaphore caps parallel short calls at about 4.
- **`Cli*` classes implement the interfaces.** They build arguments, call `WslcCli`, parse with source-generated JSON contexts, and map to Abstractions models.
- **Testing:** `WslcCli` sits behind an internal `ICliRunner` interface. Unit tests can then replay captured fixtures (from S1) without starting real processes.
### 2.3 Other cross-cutting points

- **JSON parsing:** `JsonSerializerContext` source generation only, which is AOT-safe. DTOs mirror `wslc_schema.h` in the microsoft/WSL repo, checked against captured fixtures.
- **Refresh model** (revised after S1; see `docs/spikes/S1-cli-contract.md`):
  - **The VM lifecycle matters.** The default session's VM shuts itself down about 30 s after the last container stops. A cold start costs about 2.6 s.
    - An open `wslc events` **keeps the VM alive indefinitely**, at about 1.1 GB.
    - Any `list`/`images` poll after an idle shutdown **boots the VM again**.
    - So the GUI must not keep the VM awake on its own.
  - **Events:**
    - Run `wslc events` only while at least one container is running.
    - Events cover container create/start/kill/stop/destroy and network events only. There are no `die` events and no image or volume events.
    - In 3.0.1 the output is text lines; JSON arrives in a later WSL release.
    - Use events to trigger a targeted refresh.
  - **Polling** (implemented in M1, `ResourceListViewModel`): only the page being shown is polled, and never while the window is minimized.
    - Every 4 s while containers are running.
    - When nothing is running, at most every 45 s. Any wslc command resets the VM's 30 s idle timer, so polling faster would keep the VM alive forever. This was verified in M1: 4 s polling kept the VM up; with the 45 s rule it idles out about 32 s after the last container stops.
    - Never while the VM is down. VM state is detected via the `vmmemwslc-cli-<user>` process, which doesn't wake the VM.
    - Health checks use `version`/`info`, which don't wake the VM.
  - **Status:** show an "Engine stopped (idle)" state, plus a "Starting engine…" state during the cold start.
  - **Stats:** take 1–2 s per call. Run one non-overlapping `stats --format json` loop for all containers, only while a stats view is visible.
  - **Listing:** fill grids from `list -a --no-trunc --format json` (JSON Lines). Get exact data (ports, RFC 3339 times) from one batched `inspect id1 id2 …`.
  - **Threading:** all results reach the UI via `Dispatcher.BeginInvoke`, using the TaskManager `SystemSampler` pattern.
- **Pull progress:** when stdout isn't a TTY, `pull` doesn't flush until it finishes. Live progress therefore needs ConPTY plus parsing the VT output. The MVP shows an indeterminate bar and the final output.
- **Logs:** run `logs -t`. stdout and stderr come back on separate pipes, so merge them by timestamp. For the MVP, strip ANSI escape codes; render SGR colour later.
- **Log view:** batch appends on a 50–100 ms dispatcher timer. Cap the buffer at N lines and trim the head.
- **Errors:** a central handler shows a toast or dialog with the CLI's stderr text. Also hook `Application.DispatcherUnhandledException`.

---

## 3. Feature list

### MVP (milestones M1–M3)

1. **Startup and health**
   - Check that `wslc.exe` exists, then run `wslc version` and `info`.
   - Show a helpful screen if WSL or WSLC is missing or out of date (`wsl --install` / `wsl --update` hints).
   - Status bar: engine state, WSL version, session name.
2. **Containers page**
   - Columns: name, image, status, ports, created, ID. Status badges, sorting and search.
   - Running and all toggle.
   - Row actions and context menu: start, stop, restart, kill, remove (force), and multi-select bulk actions.
   - Ports show as clickable `localhost:port` links that open in a browser.
   - "Prune stopped containers".
3. **Images page**
   - Columns: name/tag, ID, size, created.
   - Actions: remove, tag, prune, **Run…** (opens the run dialog pre-filled).
   - **Pull image** dialog with progress.
4. **Run container dialog**
   - Fields: image, name, command and args, env vars (key/value grid), port mappings, bind mounts (folder picker), named volumes, `--rm`, detach, network.
   - "Show CLI command" preview with a copy button. This doubles as a debugging aid.
5. **Container detail view.** Opened by double-clicking a container.
   - **Logs:** follow, tail N, timestamps toggle, find (Ctrl+F), clear, copy, save to file, auto-scroll lock.
   - **Inspect:** pretty-printed JSON, with copy. Read-only `MultiLineTextBox`, or a tree later.
   - **Terminal (tier T0):** an "Open in terminal" button that launches `wt.exe` (fallback: default console) with `wslc exec -it <id> sh`. Bash is preferred if present.
   - **Exec one-shot:** a single command line that runs `wslc exec <id> …` and shows its output.
6. **Light/Dark/System theme**, and remembered window size and position.

### v1 (milestones M4–M6)

7. **Embedded terminal (tier T1):** `Microsoft.Terminal.Control` in a child HWND plus ConPTY running `wslc exec -it`.
   - Resize, copy and paste, theme matched to the app, several tabs (MewDock).
   - Fallback to T0 if the DLL fails to load.
8. **Files tab**
   - Lazy `TreeView` and `GridView` of the container's filesystem:
     - **Running container:** list with `wslc exec <id> stat -c … <dir>/*`. This works with BusyBox and GNU tools; the spike confirms it.
     - **Stopped container, or distroless image:** run `wslc export` to a temp tar and browse it in-process with `System.Formats.Tar`.
   - Download file or folder (`wslc cp`), upload by drag-and-drop from Explorer (`cp` in), and a read-only text preview for small files.
9. **Stats**
   - Live CPU, memory, network and block I/O for each container, drawn with MewCharts.
   - Summary columns in the Containers list.
10. **Volumes page:** list, inspect, create, remove, prune, and "used by" (cross-referenced from inspect).
11. **Networks page:** list, inspect, create, remove, prune, connect and disconnect.
12. **Builds:**
    - "Build image from folder" (Dockerfile picker, tag, build args, no-cache) with streamed build output.
    - `save`/`load`/`import` tarballs, and `export` a container.
13. **Registries:** `login`/`logout` and push.
14. **Tray icon:** quick list of running containers with start/stop, and "Quit" vs "Close to tray".
15. **Settings page:**
    - App preferences: theme, refresh interval, default shell, terminal font.
    - "Open WSLC settings" (`wslc settings`).
    - A list of sessions (`system session list`) with a session switcher.
16. **Troubleshoot page:** version info, open log folders, restart session (`system session terminate`), copy diagnostics.

### Later / stretch

- ANSI colour in the log view, with log-level highlighting (MewvalonEdit colorizer).
- Inspect as a navigable tree; diff between two containers.
- Compose-like "stacks": WSLC has no compose. We could support a small YAML subset by orchestrating `run` calls. This is a separate design.
- An SDK-backed engine for "app-owned sessions", or switching to an official management API if Microsoft ships one.
- Tier T2 terminal (custom MewUI grid plus VT parser), only if T1 proves fragile.
- Accessibility (MewUI has no UIA), which would need upstream work.

---

## 4. Spikes (do these first; each has a timebox and exit criteria)

| # | Spike | Timebox | Questions to answer / exit criteria |
|---|---|---|---|
| **S1** ✅ | **CLI contract and sessions**. Done 2026-10-03: `docs/spikes/S1-cli-contract.md`, 93 fixtures in `tests/fixtures/cli` | 0.5–1 d | <ul><li>Capture JSON fixtures for `list -a`, `images`, `stats`, `info`, `version`, `inspect` (container, image, volume, network), `volume ls`, `network ls`. Store them in `tests/fixtures`.</li><li>What does `events` print (format, one JSON per line?), and does it stay open while the session is idle?</li><li>What does `pull` print when stdout is **not** a TTY? Can we parse progress from it?</li><li>Does the default CLI session persist after the CLI exits? What is the cold-start latency of the first command (VM boot)?</li><li>What do the exit codes and stderr look like for common errors (not found, already running)?</li><li>Does `logs -f -t` interleave stdout and stderr, and what is the timestamp format?</li></ul> |
| **S2** | **MewUI app skeleton under NativeAOT** | 1 d | <ul><li>NavigationView shell, a GridView of containers fed by S1's runner via `Task.Run` → `Dispatcher.BeginInvoke`, a context menu, and the run dialog layout.</li><li>Publish with `PublishAot` with 0 warnings.</li><li>Log-view throughput: append 10k lines/s into `MultiLineTextBox` with batching and head-trim at 50k lines. Measure CPU and responsiveness.</li><li>If it struggles, evaluate vendoring MewvalonEdit.</li></ul> |
| **S3** | **Embedded terminal** | 2 d | <ul><li>Get `Microsoft.Terminal.Control.dll` from `CI.Microsoft.Terminal.Wpf` and check that redistribution terms are acceptable.</li><li>Host it in a MewUI element, copying the WebView2/WinFormsHost child-HWND pattern.</li><li>Wire ConPTY → `wslc exec -it <id> sh`. Check `vi`, `top`, resize, copy and paste, IME, Tab key, focus moving in and out, DPI change, and NativeAOT.</li><li>**Exit:** decide between T1 and the xterm.js/WebView2 fallback.</li></ul> |
| **S4** | **Filesystem browsing** | 0.5 d | <ul><li>Check `stat -c '%F\|%s\|%Y\|%A\|%N'` listing on alpine (BusyBox), debian and ubuntu images. Cover symlinks, special characters in names and large directories.</li><li>Check the `export` + `System.Formats.Tar` path for stopped and distroless containers.</li><li>Measure `cp` in and out round-trips.</li></ul> |
| **S5** | **SDK under NativeAOT** (optional, needed only if we want the SDK engine) | 0.5 d | <ul><li>Publish with `PublishAot` and the `Microsoft.WSL.Containers` projection.</li><li>Check activation (`GetDelegateForFunctionPointer<T>`), events, awaiting `IAsyncActionWithProgress`, and `IInputStream` interop.</li><li>Check where `wslcsdk.dll` must sit (next to the exe), and look for trim warnings.</li></ul> |

| **S6** | **Direct COM to the WSL service** (optional) | 1 d | <ul><li>Write `[GeneratedComInterface]` bindings for `IWSLCSessionManager` (`GetVersion`, `ListSessions`, `OpenSessionByName`) and `IWSLCSession::ListContainers` / `ListImages`, translated from `wslc.idl` at the matching WSL tag.</li><li>Under NativeAOT, open the CLI default session and list containers.</li><li>Compare the results and latency with the CLI.</li><li>Check whether the service accepts a non-`wslc.exe` caller (impersonation and proxy blanket).</li><li>Stretch: TTY exec with `WSLCProcessFlagsTty` plus `ResizeTty`.</li><li>**Exit:** go/no-go on a version-gated `ComEngine`.</li></ul> |

S1 and S2 can run in parallel. S3 starts once S2's skeleton exists.

---

## 5. Milestones

| M | Content | Depends on |
|---|---|---|
| M0 ✅ | Repo setup: `global.json`, solution, Directory.Build.props, AOT publish script, CI build (GitHub Actions on windows-latest with `PublishAot`) | – |
| M1 ✅ | `WslcGui.Engine.Cli` with fixtures and tests; health check; Containers and Images pages (read-only), then lifecycle actions | S1, S2 |
| M2 ✅ | Run dialog, Pull dialog with progress, events-driven refresh, toasts and error handling | M1 |
| M3 ✅ | Container detail: Logs, Inspect, Exec one-shot, external terminal (T0). **MVP release.** | M2 |
| M4 | Embedded terminal (T1) | S3, M3 |
| M5 | Files tab and stats graphs | S4, M3 |
| M6 | Volumes, Networks, Builds/save/load, registries, tray icon, settings and troubleshoot pages | M3 |

---

## 6. Risks

- **WSLC is in preview.**
  - CLI output (JSON schemas, flags) may change between WSL releases.
  - Mitigations: fixtures-based tests, tolerant DTOs (optional fields), and a check of `wslc version` at startup with a "tested with" warning.
- **CLI process overhead.** Each command spawns a process (expected tens of ms).
  - Use events plus moderate polling and avoid per-row commands.
  - Batch `inspect` and `stats` calls with many IDs.
- **Terminal control is unofficial.** It is internal API distributed as an unofficial repackage.
  - Keep it behind `ITerminalView`, so T0 and xterm.js remain fallbacks.
- **MewUI is pre-1.0.** Its API changes between minor releases.
  - Pin 0.22.1 and upgrade deliberately. Note that its maintainer is active and fixes bugs quickly.
- **No accessibility in MewUI.** Accept this for now.
- **The CLI default session may need elevation parity.** An elevated CLI uses a different session (`wslc-cli-admin-<user>`).
  - Run the GUI non-elevated by default and show which session is in use.

---

## 7. Open decisions (for the user)

1. ~~Backend~~ **Decided (2026-10-03):**
   - CLI first, isolated behind the capability interfaces in §2.1.
   - No process code outside `WslcGui.Engine.Cli`; the banned-API analyzer enforces this.
   - Parts can be swapped later, for example to COM after S6.
2. **Terminal ambition for v1:** embedded T1 (Windows Terminal engine) vs external-only T0.
3. **Platforms:** x64 only, or x64 + arm64 from the start (both MewUI and WSLC support arm64).
