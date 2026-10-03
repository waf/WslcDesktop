# UI automation scripts

PowerShell scripts for driving a running WSLC Desktop from the command line: take screenshots, click, type, press keys
and open the tray menu. They're for checking UI changes by hand or by an agent. They aren't a test framework.

MewUI draws every control itself and exposes nothing to UI Automation, so the scripts work at the Win32 level instead.
They post mouse and keyboard messages to the window and capture it with `PrintWindow`. Because the input is posted,
none of this moves the real cursor or needs the window in front. All the logic is in `UiAutomation.ps1`; the other
scripts are thin wrappers around it.

| Script               | Does                                                                         |
| -------------------- | ---------------------------------------------------------------------------- |
| `Screenshot.ps1`     | Saves the main window (or `-Title` window) to a PNG                          |
| `Click.ps1`          | Clicks at `-X -Y`; `-Double`, `-Right`                                        |
| `Type.ps1`           | Types `-Text` into the focused control                                       |
| `Keys.ps1`           | Presses `-Key Enter` / `Escape` / `Down` / `Backspace`... or `-Vk <code>`; `-Repeat` |
| `List-Windows.ps1`   | Lists the app's visible top-level windows (`-IncludeHidden` for all)         |
| `Capture-Frames.ps1` | Optional click, then a burst of screenshots, to catch layout jumps and flicker |
| `Tray-Menu.ps1`      | Opens the tray menu and captures it (`-Out`), or shows the window (`-Open`)   |

Every script takes `-ProcessName` (default `WslcDesktop`). Each one except `Tray-Menu.ps1` also takes `-Title` to target
a window other than the main one.

## Running

Use Windows PowerShell 5.1 or PowerShell 7. You'll usually need to bypass the execution policy:

```sh
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/ui/Screenshot.ps1 -Out shots/main.png
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/ui/Click.ps1 -X 360 -Y 102   # the search box
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/ui/Type.ps1 -Text redis
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/ui/Keys.ps1 -Key Backspace -Repeat 5
```

From another script, dot-source `UiAutomation.ps1` and call `Save-UiScreenshot`, `Invoke-UiClick`, `Send-UiText`,
`Send-UiKey`, `Find-UiWindow` and `Get-UiWindows` directly. This loads the native interop once instead of on every call.

Keep screenshots out of the repo, in a temp or scratch directory.

## Things to know

- **Coordinates are window-relative.** `(0, 0)` is the top-left of the window frame, title bar included. This matches
  the pixels in a `Screenshot.ps1` capture, so you can read a position off the PNG and click it. The scripts handle the
  conversion to client coordinates and are DPI-aware.
- **Popups and dialogs are separate windows.** Pass `-Title` to `Screenshot.ps1`, `Click.ps1`, `Type.ps1` and
  `Keys.ps1` to target them:
  - Dialogs are titled by their caption (for example `-Title "Run container"`).
  - MewUI popups, such as autocomplete lists and dropdowns, are titled `Window`.
  - Native menus have class `#32768` and no title. Find them with `List-Windows.ps1`.
- **Typing needs a key-down per character.** MewUI suppresses `WM_CHAR` after a key-down it has handled, until the next
  key-down arrives. That's correct for real keyboards, but it swallows bare posted characters. `Send-UiText` sends an
  F24 key-down, a key with no binding, before each character. Click the text box first, because typing goes to
  whatever control has focus.
- **Close to tray hides the window.** The main window is then invisible and `Find-UiWindow` won't find it. Use
  `Tray-Menu.ps1 -Open` to bring it back. `Tray-Menu.ps1` posts the icon's callback message, so it must stay in sync
  with `TrayIcon.CallbackMessage`.
- **The tray menu is modal.** While it's open, the app's UI thread is inside the menu loop. Use `Keys.ps1` with `Down`
  and `Enter` to pick an item, or `Escape` to close it, before sending anything else.
- **Posted input isn't real input.** There's no drag support, and the window isn't activated. If something only works
  with a real mouse, that's a limit of this approach, not necessarily a bug in the app.
- **From Git Bash**, arguments that start with `/` get rewritten into Windows paths. Prefix the command with
  `MSYS_NO_PATHCONV=1` when passing one.
