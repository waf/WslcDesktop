# Presses a key in the focused control, by name or virtual-key code.
#   tools/ui/Keys.ps1 -Key Enter
#   tools/ui/Keys.ps1 -Key Backspace -Repeat 5
#   tools/ui/Keys.ps1 -Vk 0x74
# Names: Backspace, Tab, Enter, Escape, Space, PageUp, PageDown, End, Home, Left, Up, Right, Down, Delete, F5.
param([string] $Key, [int] $Vk, [int] $Repeat = 1, [string] $Title, [string] $ProcessName = 'WslcDesktop')
$ErrorActionPreference = 'Stop'
if (-not $Key -and -not $Vk) { throw 'Pass -Key <name> or -Vk <code>.' }
. $PSScriptRoot/UiAutomation.ps1
Send-UiKey -Key $Key -Vk $Vk -Repeat $Repeat -Title $Title -ProcessName $ProcessName
