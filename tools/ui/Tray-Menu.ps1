# Drives the notification-area (tray) icon without touching the taskbar, by posting the icon's callback message to the
# main window, which works even while that window is hidden (closed to tray).
#   tools/ui/Tray-Menu.ps1 -Out shots/tray.png   # opens the context menu and captures it
#   tools/ui/Tray-Menu.ps1 -Open                 # same as clicking the icon: shows and activates the main window
# The menu is modal: while it's open, choose an item with Keys.ps1 (-Key Down / Enter) or close it with -Key Escape.
param([string] $Out, [switch] $Open, [string] $ProcessName = 'WslcDesktop')
$ErrorActionPreference = 'Stop'
. $PSScriptRoot/UiAutomation.ps1

# Must match TrayIcon.CallbackMessage in src/WslcDesktop.App/Shell/TrayIcon.cs.
$callbackMessage = 0x8000 + 0x57
$main = Get-UiWindows -ProcessName $ProcessName -IncludeHidden | Where-Object Title -like '*WSLC Desktop*' | Select-Object -First 1
if (-not $main) { throw "No WSLC Desktop window found. Is $ProcessName running?" }

$event = if ($Open) { $script:WM_LBUTTONUP } else { 0x007B } # WM_CONTEXTMENU
[WslcUi.Native]::PostMessage($main.Handle, $callbackMessage, [IntPtr]0, [IntPtr]$event) | Out-Null
if ($Open -or -not $Out) { return }

# Menus are system windows of class #32768, owned by the app's UI thread.
$menu = $null
for ($i = 0; $i -lt 20 -and -not $menu; $i++) {
    Start-Sleep -Milliseconds 100
    $menu = Get-UiWindows -ProcessName $ProcessName | Where-Object Class -eq '#32768' | Select-Object -First 1
}
if (-not $menu) { throw 'The tray menu did not appear.' }

$bitmap = New-Object System.Drawing.Bitmap $menu.Width, $menu.Height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($menu.Left, $menu.Top, 0, 0, $bitmap.Size) # menus don't render through PrintWindow
$path = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
$bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose(); $bitmap.Dispose()
$path
