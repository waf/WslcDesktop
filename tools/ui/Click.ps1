# Clicks at window-relative pixel coordinates, as read off a screenshot of the same window.
#   tools/ui/Click.ps1 -X 60 -Y 140
#   tools/ui/Click.ps1 -X 300 -Y 200 -Double
#   tools/ui/Click.ps1 -X 300 -Y 200 -Right
#   tools/ui/Click.ps1 -X 120 -Y 90 -Title "Delete container"
param(
    [Parameter(Mandatory)] [int] $X,
    [Parameter(Mandatory)] [int] $Y,
    [string] $Title,
    [switch] $Double,
    [switch] $Right,
    [string] $ProcessName = 'WslcDesktop')
$ErrorActionPreference = 'Stop'
. $PSScriptRoot/UiAutomation.ps1
Invoke-UiClick -X $X -Y $Y -Title $Title -Double:$Double -Right:$Right -ProcessName $ProcessName
