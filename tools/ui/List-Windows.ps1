# Lists the app's top-level windows: the main window, open dialogs and popups (titled "Window"), and menus (#32768).
#   tools/ui/List-Windows.ps1
#   tools/ui/List-Windows.ps1 -IncludeHidden
param([switch] $IncludeHidden, [string] $ProcessName = 'WslcDesktop')
$ErrorActionPreference = 'Stop'
. $PSScriptRoot/UiAutomation.ps1
Get-UiWindows -ProcessName $ProcessName -IncludeHidden:$IncludeHidden |
    Format-Table Handle, Title, Class, Visible, Left, Top, Width, Height -AutoSize
