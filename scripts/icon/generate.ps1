<#
.SYNOPSIS
    Regenerates the BrowserFlip icon assets in assets/icon/.

.DESCRIPTION
    Builds and runs scripts/icon/IconGen (a small WPF-based renderer). Requires the .NET 10 SDK
    with the Windows desktop workload (any normal .NET SDK install on Windows has it).
    If dotnet is not on PATH, the per-user install under %LOCALAPPDATA%\Microsoft\dotnet is used.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/icon/generate.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $PSScriptRoot 'IconGen\IconGen.csproj'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'assets\icon' }

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $dotnetExe = $dotnet.Source
} else {
    $userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    $dotnetExe = Join-Path $userDotnet 'dotnet.exe'
    if (-not (Test-Path $dotnetExe)) { throw 'dotnet SDK not found.' }
    $env:DOTNET_ROOT = $userDotnet
}

& $dotnetExe run --project $project -c Release -- $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Icon generation failed (exit $LASTEXITCODE)." }
