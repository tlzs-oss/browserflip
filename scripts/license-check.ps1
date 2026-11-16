<#
.SYNOPSIS
    Fails if any NuGet package (direct or transitive) is not on the license allowlist.

.DESCRIPTION
    Runs `dotnet list package --include-transitive --format json` against the solution (packages must
    already be restored) and checks every resolved package id against scripts/license-allowlist.json.

    The check fails when:
      * a package id is not listed in the allowlist (unknown package), or
      * the license recorded for a package is not in "allowedLicenses", or
      * -VerifyOnline is given and the license expression in the resolved version's nuspec on
        nuget.org differs from the allowlist (or the nuspec has no SPDX expression and the resolved
        version is not the version that was manually verified).

.PARAMETER Target
    Solution or project to inspect. Defaults to BrowserFlip.slnx at the repository root.

.PARAMETER AllowlistPath
    Path to the allowlist JSON. Defaults to license-allowlist.json next to this script.

.PARAMETER VerifyOnline
    Also re-read each resolved package's nuspec from nuget.org and compare its license.

.EXAMPLE
    dotnet restore BrowserFlip.slnx
    pwsh ./scripts/license-check.ps1 -VerifyOnline
#>
[CmdletBinding()]
param(
    [string] $Target,
    [string] $AllowlistPath,
    [switch] $VerifyOnline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Target) { $Target = Join-Path $repoRoot 'BrowserFlip.slnx' }
if (-not $AllowlistPath) { $AllowlistPath = Join-Path $PSScriptRoot 'license-allowlist.json' }

if (-not (Test-Path -LiteralPath $Target)) { throw "Target not found: $Target" }
if (-not (Test-Path -LiteralPath $AllowlistPath)) { throw "Allowlist not found: $AllowlistPath" }

$allowlist = Get-Content -LiteralPath $AllowlistPath -Raw | ConvertFrom-Json
$allowedLicenses = @($allowlist.allowedLicenses)
$entries = @{}
foreach ($prop in $allowlist.packages.PSObject.Properties) {
    $entries[$prop.Name.ToLowerInvariant()] = $prop.Value
}

$raw = & dotnet list $Target package --include-transitive --format json
if ($LASTEXITCODE -ne 0) {
    $raw | Write-Host
    throw "dotnet list package failed with exit code $LASTEXITCODE (did you run dotnet restore?)"
}
$report = ($raw -join "`n") | ConvertFrom-Json

if ($report.PSObject.Properties.Name -contains 'problems' -and $report.problems) {
    foreach ($p in $report.problems) { Write-Host "::error::dotnet list package problem: $($p.text)" }
    throw 'dotnet list package reported problems.'
}

# Collect distinct id@version pairs across all projects and frameworks.
$resolved = @{}
foreach ($project in @($report.projects)) {
    if (-not ($project.PSObject.Properties.Name -contains 'frameworks')) { continue }
    foreach ($fw in @($project.frameworks)) {
        foreach ($kind in 'topLevelPackages', 'transitivePackages') {
            if (-not ($fw.PSObject.Properties.Name -contains $kind)) { continue }
            foreach ($pkg in @($fw.$kind)) {
                $key = "$($pkg.id.ToLowerInvariant())@$($pkg.resolvedVersion)"
                $resolved[$key] = [pscustomobject]@{ Id = $pkg.id; Version = $pkg.resolvedVersion }
            }
        }
    }
}

function Get-NuspecLicense([string] $id, [string] $version) {
    $lid = $id.ToLowerInvariant()
    $lver = $version.ToLowerInvariant()
    $url = "https://api.nuget.org/v3-flatcontainer/$lid/$lver/$lid.nuspec"
    [xml] $nuspec = (Invoke-WebRequest -Uri $url -UseBasicParsing).Content
    $ns = New-Object System.Xml.XmlNamespaceManager($nuspec.NameTable)
    $ns.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
    $node = $nuspec.SelectSingleNode('/n:package/n:metadata/n:license', $ns)
    if ($node -and $node.GetAttribute('type') -eq 'expression') { return $node.InnerText.Trim() }
    return $null
}

$failures = New-Object System.Collections.Generic.List[string]
$rows = foreach ($item in ($resolved.Values | Sort-Object Id, Version)) {
    $entry = $entries[$item.Id.ToLowerInvariant()]
    $status = 'OK'
    $license = $null
    if (-not $entry) {
        $status = 'UNKNOWN PACKAGE'
        $failures.Add("$($item.Id) $($item.Version): not in $([IO.Path]::GetFileName($AllowlistPath))")
    }
    else {
        $license = [string] $entry.license
        if ($allowedLicenses -notcontains $license) {
            $status = 'LICENSE NOT ALLOWED'
            $failures.Add("$($item.Id) $($item.Version): license '$license' is not in allowedLicenses")
        }
        elseif ($VerifyOnline) {
            $online = Get-NuspecLicense $item.Id $item.Version
            if ($online) {
                if ($online -ne $license) {
                    $status = 'LICENSE MISMATCH'
                    $failures.Add("$($item.Id) $($item.Version): nuget.org says '$online', allowlist says '$license'")
                }
            }
            elseif ($item.Version -ne [string] $entry.verifiedVersion) {
                $status = 'NEEDS MANUAL REVIEW'
                $failures.Add("$($item.Id) $($item.Version): nuspec has no SPDX expression and only version $($entry.verifiedVersion) was manually verified")
            }
        }
    }
    [pscustomobject]@{ Package = $item.Id; Version = $item.Version; License = $license; Status = $status }
}

if ($rows) { $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host }
Write-Host "Checked $(@($rows).Count) package(s) against $AllowlistPath$(if ($VerifyOnline) { ' (online verification on)' })."

if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "::error::License check: $f" }
    exit 1
}
Write-Host 'License check passed.'
