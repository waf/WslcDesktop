# Shared helpers for driving WSLC Desktop from scripts: find its windows, click, type, press keys, capture.
# Dot-source it (`. $PSScriptRoot/UiAutomation.ps1`); the other scripts in this folder do.
#
# MewUI draws its own controls and has no UI Automation support, so these helpers post Win32 input messages to the
# window and capture it with PrintWindow. Coordinates are window-relative pixels, the same as in a capture made by
# Save-UiScreenshot (0,0 is the top-left of the window frame), so you can read a position off a screenshot and click it.

if (-not ('WslcUi.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WslcUi
{
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        public static List<IntPtr> WindowsOf(uint pid, bool visibleOnly)
        {
            var result = new List<IntPtr>();
            EnumWindows((hwnd, _) =>
            {
                uint owner;
                GetWindowThreadProcessId(hwnd, out owner);
                if (owner == pid && (!visibleOnly || IsWindowVisible(hwnd))) result.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static string Text(IntPtr hwnd) { var s = new StringBuilder(512); GetWindowText(hwnd, s, s.Capacity); return s.ToString(); }
        public static string Class(IntPtr hwnd) { var s = new StringBuilder(256); GetClassName(hwnd, s, s.Capacity); return s.ToString(); }
    }
}
'@
}

Add-Type -AssemblyName System.Drawing
# Without this, captures and coordinates are scaled on displays that aren't at 100%.
[WslcUi.Native]::SetProcessDPIAware() | Out-Null

$script:WM_MOUSEMOVE = 0x0200; $script:WM_LBUTTONDOWN = 0x0201; $script:WM_LBUTTONUP = 0x0202; $script:WM_LBUTTONDBLCLK = 0x0203
$script:WM_RBUTTONDOWN = 0x0204; $script:WM_RBUTTONUP = 0x0205
$script:WM_KEYDOWN = 0x0100; $script:WM_KEYUP = 0x0101; $script:WM_CHAR = 0x0102

function Get-UiWindows {
    <# Lists the app's top-level windows (main window, dialogs, popups), visible ones by default. #>
    param([string] $ProcessName = 'WslcDesktop', [switch] $IncludeHidden)
    foreach ($process in Get-Process $ProcessName -ErrorAction SilentlyContinue) {
        foreach ($hwnd in [WslcUi.Native]::WindowsOf([uint32]$process.Id, -not $IncludeHidden)) {
            $rect = New-Object WslcUi.Native+RECT
            [WslcUi.Native]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
            [pscustomobject]@{
                Handle  = $hwnd
                Title   = [WslcUi.Native]::Text($hwnd)
                Class   = [WslcUi.Native]::Class($hwnd)
                Visible = [WslcUi.Native]::IsWindowVisible($hwnd)
                Left    = $rect.Left
                Top     = $rect.Top
                Width   = $rect.Right - $rect.Left
                Height  = $rect.Bottom - $rect.Top
            }
        }
    }
}

function Find-UiWindow {
    <#
    Finds one app window. With -Title, the first visible window whose title contains it (dialogs use their title;
    MewUI popups such as autocomplete lists are titled "Window"). Without it, the main window ("WSLC Desktop").
    #>
    param([string] $Title, [string] $ProcessName = 'WslcDesktop')
    $match = if ($Title) { $Title } else { 'WSLC Desktop' }
    $window = Get-UiWindows -ProcessName $ProcessName | Where-Object { $_.Title -like "*$match*" } | Select-Object -First 1
    if (-not $window) {
        $open = (Get-UiWindows -ProcessName $ProcessName | ForEach-Object { "'$($_.Title)'" }) -join ', '
        $hint = if ($open) { "Open windows: $open." } else { "Is $ProcessName running and visible? (With 'close to tray' on, a closed window is only hidden.)" }
        throw "No window titled '*$match*'. $hint"
    }
    $window
}

function ConvertTo-ClientLParam {
    <# Window-relative (x, y) -> client coordinates packed into an lParam, as mouse messages expect. #>
    param([IntPtr] $Handle, [int] $X, [int] $Y)
    $rect = New-Object WslcUi.Native+RECT
    [WslcUi.Native]::GetWindowRect($Handle, [ref]$rect) | Out-Null
    $point = New-Object WslcUi.Native+POINT
    $point.X = $rect.Left + $X
    $point.Y = $rect.Top + $Y
    [WslcUi.Native]::ScreenToClient($Handle, [ref]$point) | Out-Null
    [IntPtr](($point.Y -shl 16) -bor ($point.X -band 0xFFFF))
}

function Invoke-UiClick {
    <# Clicks at window-relative (x, y). -Double sends a real double-click sequence; -Right a right-click. #>
    param([int] $X, [int] $Y, [string] $Title, [switch] $Double, [switch] $Right, [string] $ProcessName = 'WslcDesktop')
    $h = (Find-UiWindow -Title $Title -ProcessName $ProcessName).Handle
    $l = ConvertTo-ClientLParam -Handle $h -X $X -Y $Y
    $down = if ($Right) { $script:WM_RBUTTONDOWN } else { $script:WM_LBUTTONDOWN }
    $up = if ($Right) { $script:WM_RBUTTONUP } else { $script:WM_LBUTTONUP }
    $buttons = if ($Right) { 2 } else { 1 }
    [WslcUi.Native]::PostMessage($h, $script:WM_MOUSEMOVE, [IntPtr]0, $l) | Out-Null
    [WslcUi.Native]::PostMessage($h, $down, [IntPtr]$buttons, $l) | Out-Null
    Start-Sleep -Milliseconds 50
    [WslcUi.Native]::PostMessage($h, $up, [IntPtr]0, $l) | Out-Null
    if ($Double) {
        Start-Sleep -Milliseconds 50
        [WslcUi.Native]::PostMessage($h, $script:WM_LBUTTONDBLCLK, [IntPtr]$buttons, $l) | Out-Null
        Start-Sleep -Milliseconds 50
        [WslcUi.Native]::PostMessage($h, $up, [IntPtr]0, $l) | Out-Null
    }
}

function Send-UiText {
    <#
    Types text into the focused control of a window (click the text box first).
    Each character is preceded by a key-down for an unused key (F24). MewUI drops typed characters after a handled
    key-down until the next key-down arrives; real typing always sends one per character, and so must we.
    #>
    param([string] $Text, [string] $Title, [string] $ProcessName = 'WslcDesktop')
    $h = (Find-UiWindow -Title $Title -ProcessName $ProcessName).Handle
    foreach ($c in $Text.ToCharArray()) {
        [WslcUi.Native]::PostMessage($h, $script:WM_KEYDOWN, [IntPtr]0x87, [IntPtr]1) | Out-Null
        [WslcUi.Native]::PostMessage($h, $script:WM_CHAR, [IntPtr][int]$c, [IntPtr]1) | Out-Null
        Start-Sleep -Milliseconds 15
    }
}

$script:VirtualKeys = @{
    Backspace = 0x08; Tab = 0x09; Enter = 0x0D; Escape = 0x1B; Space = 0x20; PageUp = 0x21; PageDown = 0x22
    End = 0x23; Home = 0x24; Left = 0x25; Up = 0x26; Right = 0x27; Down = 0x28; Delete = 0x2E; F5 = 0x74
}

function Send-UiKey {
    <# Presses a key (by name, or -Vk for a virtual-key code) in a window, -Repeat times. #>
    param([string] $Key, [int] $Vk, [int] $Repeat = 1, [string] $Title, [string] $ProcessName = 'WslcDesktop')
    if ($Key) {
        if (-not $script:VirtualKeys.ContainsKey($Key)) { throw "Unknown key '$Key'. Known: $($script:VirtualKeys.Keys -join ', '). Or pass -Vk." }
        $Vk = $script:VirtualKeys[$Key]
    }
    $h = (Find-UiWindow -Title $Title -ProcessName $ProcessName).Handle
    for ($i = 0; $i -lt $Repeat; $i++) {
        [WslcUi.Native]::PostMessage($h, $script:WM_KEYDOWN, [IntPtr]$Vk, [IntPtr]1) | Out-Null
        Start-Sleep -Milliseconds 20
        [WslcUi.Native]::PostMessage($h, $script:WM_KEYUP, [IntPtr]$Vk, [IntPtr]0xC0000001) | Out-Null
        Start-Sleep -Milliseconds 20
    }
}

function Save-UiScreenshot {
    <#
    Captures one window to a PNG with PrintWindow, so it works even when other windows cover it. Popups (menus,
    autocomplete lists) are separate windows: capture them with -Title "Window".
    #>
    param([Parameter(Mandatory)] [string] $Out, [string] $Title, [string] $ProcessName = 'WslcDesktop')
    $window = Find-UiWindow -Title $Title -ProcessName $ProcessName
    $bitmap = New-Object System.Drawing.Bitmap $window.Width, $window.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    [WslcUi.Native]::PrintWindow($window.Handle, $hdc, 2) | Out-Null # PW_RENDERFULLCONTENT: needed for Direct2D content
    $graphics.ReleaseHdc($hdc)
    $directory = Split-Path -Parent ([IO.Path]::GetFullPath($Out))
    if ($directory) { New-Item -ItemType Directory -Force $directory | Out-Null }
    $bitmap.Save([IO.Path]::GetFullPath($Out), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    [pscustomobject]@{ Path = [IO.Path]::GetFullPath($Out); Width = $window.Width; Height = $window.Height; Title = $window.Title }
}
