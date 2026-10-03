# Types text into the focused control. Click the text box first.
#   tools/ui/Type.ps1 -Text "postgres"
param([Parameter(Mandatory)] [string] $Text, [string] $Title, [string] $ProcessName = 'WslcDesktop')
$ErrorActionPreference = 'Stop'
. $PSScriptRoot/UiAutomation.ps1
Send-UiText -Text $Text -Title $Title -ProcessName $ProcessName
