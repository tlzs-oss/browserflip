<#
.SYNOPSIS
    Copies the license and notice files of the .NET runtime packs embedded in a self-contained
    BrowserFlip build into the release folder, and writes THIRD-PARTY-NOTICES.txt listing them.

.DESCRIPTION
    BrowserFlip has no third-party NuGet package dependencies, but a self-contained build embeds
    the .NET runtime (Microsoft.NETCore.App) and the Windows Desktop runtime (WPF,
    Microsoft.WindowsDesktop.App), including their native redistributables. This script:

      * asks MSBuild which runtime packs (id, version, folder) the build used for the runtime
        identifier, so the notices always match the shipped version;
      * copies each pack's own LICENSE / THIRD-PARTY-NOTICES files unchanged into <Output>\licenses;
      * the Windows Desktop runtime pack ships only its LICENSE: the third-party notices of WPF are
        taken from the dotnet/wpf repository at the tag of the same version (v<version>);
      * writes <Output>\THIRD-PARTY-NOTICES.txt with the components, versions, licenses (from the
        pack nuspec), the native files of each pack and the copied files.

    The script fails if a pack, a LICENSE file or the WPF notices cannot be found, so a release
    is never built without them. Works on Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
    pwsh ./scripts/Write-ThirdPartyNotices.ps1 -Project src/BrowserFlip.App/BrowserFlip.App.csproj `
        -RuntimeIdentifier win-x64 -OutputDirectory publish/win-x64 -Label v0.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Project,
    [Parameter(Mandatory)] [string] $RuntimeIdentifier,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [string] $Label,
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$json = & dotnet msbuild $Project -nologo -restore -t:ResolveFrameworkReferences `
    "-p:Configuration=$Configuration" "-p:RuntimeIdentifier=$RuntimeIdentifier" -p:SelfContained=true `
    -getItem:ResolvedRuntimePack
if ($LASTEXITCODE -ne 0) { throw "MSBuild could not resolve the runtime packs for $RuntimeIdentifier" }
$packs = @((($json | Out-String) | ConvertFrom-Json).Items.ResolvedRuntimePack)

$licenses = Join-Path $OutputDirectory 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null

$components = @(
    @{ Framework = 'Microsoft.NETCore.App'; Name = '.NET runtime'; Source = 'https://github.com/dotnet/runtime'; NoticesRepo = $null },
    @{ Framework = 'Microsoft.WindowsDesktop.App'; Name = 'Windows Desktop runtime (WPF)'; Source = 'https://github.com/dotnet/wpf'; NoticesRepo = 'dotnet/wpf' }
)

$sections = @()
foreach ($c in $components) {
    $pack = @($packs | Where-Object { $_.FrameworkName -eq $c.Framework })
    if ($pack.Count -ne 1) { throw "Runtime pack for $($c.Framework) ($RuntimeIdentifier) not found (found $($pack.Count))" }
    $pack = $pack[0]
    $id = $pack.NuGetPackageId
    $version = $pack.NuGetPackageVersion
    $dir = $pack.PackageDirectory
    if (-not (Test-Path $dir)) { throw "Runtime pack folder not found: $dir" }

    $nuspecPath = Join-Path $dir "$($id.ToLowerInvariant()).nuspec"
    if (-not (Test-Path $nuspecPath)) { throw "nuspec not found: $nuspecPath" }
    [xml] $nuspec = Get-Content -Raw -Encoding UTF8 $nuspecPath
    $metadata = $nuspec.DocumentElement.SelectSingleNode("*[local-name()='metadata']")
    $field = { param($name) $node = $metadata.SelectSingleNode("*[local-name()='$name']"); if ($node) { $node.InnerText.Trim() } else { '' } }
    $license = & $field 'license'
    if (-not $license) { $license = & $field 'licenseUrl' }
    if (-not $license) { throw "No license in $nuspecPath" }
    $copyright = & $field 'copyright'

    $copied = @()
    $files = @(Get-ChildItem $dir -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.txt)?$' } | Sort-Object Name)
    if (-not ($files | Where-Object { $_.BaseName -eq 'LICENSE' })) { throw "No LICENSE file in $dir" }
    foreach ($f in $files) {
        $name = "$id.$version.$($f.BaseName.ToUpperInvariant()).txt"
        Copy-Item $f.FullName (Join-Path $licenses $name)
        $copied += "licenses/$name (from the $id $version package)"
    }

    if ($c.NoticesRepo -and -not ($files | Where-Object { $_.BaseName -eq 'THIRD-PARTY-NOTICES' })) {
        $url = "https://raw.githubusercontent.com/$($c.NoticesRepo)/v$version/THIRD-PARTY-NOTICES.TXT"
        $name = "$($c.NoticesRepo.Replace('/', '-')).v$version.THIRD-PARTY-NOTICES.txt"
        $target = Join-Path $licenses $name
        Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing
        if ((Get-Item $target).Length -eq 0) { throw "Empty notices file from $url" }
        $hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
        $copied += "licenses/$name (from $url, SHA-256 $hash)"
    }

    $native = @()
    $nativeDir = Join-Path $dir "runtimes/$RuntimeIdentifier/native"
    if (Test-Path $nativeDir) { $native = @(Get-ChildItem $nativeDir -File | Sort-Object Name | ForEach-Object { $_.Name }) }

    $lines = @(
        "$($c.Name): $id $version",
        "  License:   $license",
        "  Copyright: $copyright",
        "  Source:    $($c.Source)"
    )
    if ($native.Count) { $lines += "  Native files in the package: $($native -join ', ')" }
    $lines += '  License and notice files:'
    $lines += $copied | ForEach-Object { "    - $_" }
    $sections += ($lines -join "`n")
}

$header = @(
    "BrowserFlip $Label ($RuntimeIdentifier) - third-party notices",
    '',
    'BrowserFlip is licensed under the MIT License (see LICENSE.txt).',
    'BrowserFlip has no third-party NuGet package dependencies. This self-contained build embeds the',
    'Microsoft .NET components listed below (the managed and native files of these runtime packs that',
    'the application uses). Their license and notice files are included unchanged in the "licenses" folder.',
    ''
) -join "`n"
$text = $header + "`n" + ($sections -join "`n`n") + "`n"
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.txt'), $text, (New-Object Text.UTF8Encoding($false)))
Write-Host $text
