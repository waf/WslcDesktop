# Captures the main window (or the window whose title contains -Title) to a PNG.
#   tools/ui/Screenshot.ps1 -Out shots/main.png
#   tools/ui/Screenshot.ps1 -Out shots/popup.png -Title Window
param([Parameter(Mandatory)] [string] $Out, [string] $Title, [string] $ProcessName = 'WslcDesktop')
$ErrorActionPreference = 'Stop'
. $PSScriptRoot/UiAutomation.ps1
Save-UiScreenshot -Out $Out -Title $Title -ProcessName $ProcessName
