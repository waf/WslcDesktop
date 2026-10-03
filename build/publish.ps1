#requires -Version 7
<#
.SYNOPSIS
    Publishes WslcGui as a NativeAOT executable.
.EXAMPLE
    ./build/publish.ps1                 # win-x64 → artifacts/publish/win-x64
    ./build/publish.ps1 -Runtime win-arm64
#>
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# The ILCompiler link step locates the MSVC linker via vswhere.exe, which isn't on PATH in every shell.
$installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path $installer) -and -not ($env:PATH -split ';' -contains $installer)) {
    $env:PATH = "$env:PATH;$installer"
}

$output = Join-Path $root "artifacts/publish/$Runtime"
dotnet publish (Join-Path $root 'src/WslcGui.App/WslcGui.App.csproj') -c $Configuration -r $Runtime -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $output 'WslcGui.exe'
'{0} ({1:N1} MB)' -f $exe, ((Get-Item $exe).Length / 1MB)
