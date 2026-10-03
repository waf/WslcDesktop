# Optionally clicks, then captures a burst of screenshots: for catching layout jumps, flicker, and transient states
# that a single screenshot taken afterwards would miss.
#   tools/ui/Capture-Frames.ps1 -X 60 -Y 180 -Prefix shots/volumes -Count 8 -DelayMs 60
# writes shots/volumes-00.png .. shots/volumes-07.png.
param(
    [Parameter(Mandatory)] [string] $Prefix,
    [int] $X = -1,
    [int] $Y = -1,
    [int] $Count = 6,
    [int] $DelayMs = 80,
    [string] $Title,
    [string] $ProcessName = 'WslcDesktop')
$ErrorActionPreference = 'Stop'
. $PSScriptRoot/UiAutomation.ps1
if ($X -ge 0 -and $Y -ge 0) { Invoke-UiClick -X $X -Y $Y -Title $Title -ProcessName $ProcessName }
for ($i = 0; $i -lt $Count; $i++) {
    Save-UiScreenshot -Out ('{0}-{1:D2}.png' -f $Prefix, $i) -Title $Title -ProcessName $ProcessName | Select-Object -ExpandProperty Path
    Start-Sleep -Milliseconds $DelayMs
}
