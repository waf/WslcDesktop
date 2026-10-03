# AGENTS.md

Notes for AI agents working in this repo. Read `README.md` first for the features, requirements and project layout. This file covers the rules and pitfalls that aren't obvious from the code.

## Commands

```powershell
dotnet build WslcDesktop.slnx
dotnet test --solution WslcDesktop.slnx                     # all tests (Microsoft.Testing.Platform, xUnit v3)
dotnet test --solution WslcDesktop.slnx --filter-method "*Some_test_name*"
dotnet run --project src/WslcDesktop.App
./build/publish.ps1                                         # NativeAOT → artifacts/publish/win-x64/WslcDesktop.exe
```

- Tests use Microsoft.Testing.Platform (`global.json`), so `dotnet test` needs `--solution` or `--project`. Exit code 8 means "zero tests ran" in one test project. A filter that matches tests in only one project gives exit 8 even when everything passed.
- `dotnet test` leaves an empty `TestResults/` folder in the working directory. It's gitignored. Ignore it or delete it.
- **Publish with `build/publish.ps1`, not plain `dotnet publish`.** The NativeAOT link step calls `vswhere.exe` from PATH. Outside a VS Developer shell, a plain `dotnet publish` fails with `'vswhere.exe' is not recognized`. The script adds the VS Installer folder to PATH. CI calls the same script.
- `TreatWarningsAsErrors` is on, with `AnalysisLevel` `latest-recommended`, so analyzer warnings break the build. Fix them; don't suppress them without a reason.

## Architecture rules

- **Dependency direction:** `App` → `Core` → `Engine.Abstractions` ← `Engine.Cli`. `Core` (view models) knows only the abstractions. Only `src/WslcDesktop.App/Program.cs`, the composition root, references `Engine.Cli`.
- **Only `WslcDesktop.Engine.Cli` may start processes.** `BannedSymbols.txt` and the banned-API analyzer make `System.Diagnostics.Process` and `ProcessStartInfo` build errors in every other project. Within `Engine.Cli`, every `wslc` call goes through `ICliRunner` (implemented by `WslcCli`).
- **Pass arguments as lists.** Never build a command line by joining strings. Secrets (registry passwords) go to `wslc` on stdin (`RunWithInputAsync`), never in arguments.
- **Keep everything NativeAOT-clean.** Library projects have `IsAotCompatible`. Use source-generated JSON (`[JsonSerializable]` contexts, as in `CliJson.cs` and `AppSettings.cs`). Don't use reflection-based serialization or anything else that needs dynamic code.
- **Don't keep the WSLC VM awake.** Background polling slows down when nothing runs and stops while the VM is idle. Event and stats streams run only while containers run. Dialogs build their suggestions from lists the pages already loaded. New features must not add `wslc` calls that run on a timer or when a dialog opens.

## Threading

- View models must work without a `SynchronizationContext`. In the app, MewUI's Win32 dispatcher sets one; in tests there is none.
- **Don't use `Progress<T>` in `Core`.** Use `OrderedProgress<T>` (`src/WslcDesktop.Core/OrderedProgress.cs`). Without a context, `Progress<T>` runs every report on the thread pool, out of order and concurrently. That caused a flaky build-output test.
- Engine events (`CommandCompleted`, container events, stats) arrive on background threads. The App marshals them with `Application.Current.Dispatcher.BeginInvoke`.

## Tests

- `tests/WslcDesktop.Engine.Cli.Tests` tests `wslc` parsing and argument building against `FakeCliRunner`. It matches **exact** argument strings and throws `No canned result for 'wslc …'` for anything else.
- `tests/WslcDesktop.Core.Tests` tests view models against the fakes in `Fakes.cs`.
- Both test projects can see internals (`InternalsVisibleTo`).
- Test names are sentences with underscores (`Build_streams_output_and_reports_success`). CA1707 is turned off for tests.
- **Don't edit `tests/fixtures/cli`.** These files are real captured `wslc` output, kept byte for byte. Some `*.txt` captures contain `# --- stdout (N bytes)` headers that must stay correct. They contain old names on purpose (`wslcgui-s1-pg`, `com.wslcgui.spike=s1`), left over from before the project was renamed from WslcGui. Don't "fix" those names. If a test needs new output, capture it from a real `wslc` and add a new file.
- Don't use sleeps or polling (`Task.Delay` loops) to wait for async work in tests. Make the code under test deterministic.

## Files and style

- Follow `.editorconfig`: file-scoped namespaces (a warning, so a build error), 4-space C#, 2-space XML/JSON/YAML.
- **Line endings are mixed** (some files CRLF, some LF) and there's no `.gitattributes`. Keep each file's existing endings. Git Bash `sed -i` converts CRLF files to LF, so after a scripted edit check `git diff --stat`. A whole-file diff means the line endings changed.
- Match the surrounding comment density. Comments explain *why*, briefly. Don't add comments that point at planning documents. The old `PLAN.md` and `docs/spikes` were deleted.
- `src/WslcDesktop.App/Icons/IconData.g.cs` is generated. Don't edit it by hand; see `tools/README.md`.

## Checking UI changes

Unit tests don't cover the App project. To see what a change looks like, run the app and drive it with the PowerShell scripts in `tools/ui/`. They take screenshots, click at pixel positions, type into text boxes, press keys and open the tray menu. See `tools/ui/README.md` for the details and pitfalls. The basic loop is: screenshot, read coordinates off the PNG, click or type, then screenshot again.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/ui/Screenshot.ps1 -Out $env:TEMP/wslc/main.png
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/ui/Click.ps1 -X 80 -Y 132    # window-relative, as in the PNG
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/ui/Type.ps1 -Text redis
```

- MewUI has no UI Automation support. That's why the scripts post Win32 messages instead of using accessibility APIs.
- Popups, menus and dialogs are separate windows. Target them with `-Title`, and use `List-Windows.ps1` to see what's open.
- Save screenshots outside the repo.
- These scripts click real buttons in the app, and those buttons run real `wslc` commands. Don't click Remove, Prune or similar against containers, images or volumes you didn't create yourself.

## Useful locations

- `ref/MewUI/`: a gitignored clone of the MewUI source (the UI framework, package version in `Directory.Packages.props`). Read it to learn MewUI APIs and controls. The Gallery sample is the best reference, and `tools/extract-icons.py` needs it. It isn't built.
- App settings: `%LOCALAPPDATA%\WslcDesktop\settings.json`.
- `WslcDesktop_WSLC_PATH` environment variable: points the app at a different `wslc.exe`. Set it to a missing path to test the "WSLC not installed" state.
- `artifacts/` (publish output) and `TestResults/` are gitignored build output.

## Git

- Commit messages: an imperative subject line, then a short wrapped body that explains why. Keep unrelated changes in separate commits.
