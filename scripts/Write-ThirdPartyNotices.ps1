<#
.SYNOPSIS
    Copies the license and notice files of the .NET runtime packs embedded in a self-contained
    BrowserFlip build into the release folder, and writes THIRD-PARTY-NOTICES.txt listing them.

.DESCRIPTION
    BrowserFlip has no third-party NuGet package dependencies, but a self-contained build embeds
    the .NET runtime (Microsoft.NETCore.App) and the Windows Desktop runtime (WPF,
    Microsoft.WindowsDesktop.App), including their native redistributables. This script:

      * asks MSBuild which runtime packs (id, version, folder) the build used for the runtime
        identifier, and checks them against the runtimeconfig.json of the published build, so the
        notices always match the shipped version;
      * copies each pack's own LICENSE / THIRD-PARTY-NOTICES files unchanged into <Output>\licenses;
      * the Windows Desktop runtime pack ships only its LICENSE: the third-party notices of
        dotnet/wpf and dotnet/winforms (the pack also contains assemblies built from winforms) are
        downloaded at the tag of the same version (v<version>) and must match the SHA-256 reviewed
        and recorded in $knownNotices;
      * writes <Output>\THIRD-PARTY-NOTICES.txt with the components, versions, licenses (from the
        pack nuspec), the native files of each pack and the copied files.

    The script fails if a pack, a LICENSE or THIRD-PARTY-NOTICES file, or a reviewed hash is missing,
    or if a version or hash does not match, so a release is never built without them. When the
    runner's .NET SDK moves to a new runtime patch, add the new $knownNotices entries after reviewing
    the notices files. Works on Windows PowerShell 5.1 and PowerShell 7.

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

# Notices that are not inside a runtime pack are downloaded from the source repository at the tag of the
# pack version, and must match a SHA-256 reviewed and recorded here. A new runtime version therefore fails
# the build until its notices have been reviewed and added (the pull-request release build shows it early).
$knownNotices = @{
    'dotnet/wpf@10.0.15'      = 'd23dba2fee20c6b59d8f7088ee9ed372459c952a319b7ca90f3eec370cc76eb9'
    'dotnet/winforms@10.0.15' = '5a412b38efae0b162dc0893b319023f4cd14a8985a0f9f9ae01ac052b37a36eb'
}

$output = & dotnet msbuild $Project -nologo -restore -t:ResolveFrameworkReferences `
    "-p:Configuration=$Configuration" "-p:RuntimeIdentifier=$RuntimeIdentifier" -p:SelfContained=true `
    -getItem:ResolvedRuntimePack
$raw = $output | Out-String
if ($LASTEXITCODE -ne 0) { throw "MSBuild could not resolve the runtime packs for $RuntimeIdentifier`n$raw" }
try {
    $packs = @(($raw | ConvertFrom-Json).Items.ResolvedRuntimePack)
} catch {
    throw "Unexpected MSBuild output for $RuntimeIdentifier (expected JSON): $($_.Exception.Message)`n$raw"
}

# The versions the build actually embedded (runtimeconfig.json of the self-contained build output).
$projectDir = Split-Path -Parent (Resolve-Path $Project)
$runtimeConfigs = @(Get-ChildItem (Join-Path $projectDir "bin/$Configuration") -Recurse -Filter '*.runtimeconfig.json' -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq $RuntimeIdentifier })
if ($runtimeConfigs.Count -ne 1) { throw "Expected one runtimeconfig.json of the $RuntimeIdentifier build under $projectDir\bin\$Configuration (found $($runtimeConfigs.Count)); publish first" }
$embedded = @{}
foreach ($fw in (Get-Content -Raw $runtimeConfigs[0].FullName | ConvertFrom-Json).runtimeOptions.includedFrameworks) { $embedded[$fw.name] = $fw.version }

$licenses = Join-Path $OutputDirectory 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null

# The Windows Desktop pack contains WPF (dotnet/wpf) and shared assemblies built from dotnet/winforms
# (System.Drawing.Common, System.Private.Windows.*, Microsoft.VisualBasic, Accessibility, ...).
$components = @(
    @{ Framework = 'Microsoft.NETCore.App'; Name = '.NET runtime'; Source = 'https://github.com/dotnet/runtime'; NoticesRepos = @() },
    @{ Framework = 'Microsoft.WindowsDesktop.App'; Name = 'Windows Desktop runtime (WPF)'; Source = 'https://github.com/dotnet/wpf, https://github.com/dotnet/winforms'; NoticesRepos = @('dotnet/wpf', 'dotnet/winforms') }
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
    if (-not $embedded.ContainsKey($c.Framework)) { throw "$($c.Framework) is not in the $RuntimeIdentifier runtimeconfig.json" }
    if ($embedded[$c.Framework] -ne $version) { throw "$($c.Framework): resolved pack $version but the build embedded $($embedded[$c.Framework])" }

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

    $packHasNotices = [bool]($files | Where-Object { $_.BaseName -eq 'THIRD-PARTY-NOTICES' })
    if (-not $packHasNotices -and $c.NoticesRepos.Count -eq 0) { throw "No THIRD-PARTY-NOTICES file in $dir" }
    if (-not $packHasNotices) {
        foreach ($repo in $c.NoticesRepos) {
            $key = "$repo@$version"
            if (-not $knownNotices.ContainsKey($key)) {
                throw "No reviewed SHA-256 for the $repo notices of ${version}: review https://github.com/$repo/blob/v$version/THIRD-PARTY-NOTICES.TXT and add it to `$knownNotices"
            }
            $url = "https://raw.githubusercontent.com/$repo/v$version/THIRD-PARTY-NOTICES.TXT"
            $name = "$($repo.Replace('/', '-')).v$version.THIRD-PARTY-NOTICES.txt"
            $target = Join-Path $licenses $name
            for ($attempt = 1; ; $attempt++) {
                try { Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing; break }
                catch { if ($attempt -ge 3) { throw "Could not download $url : $($_.Exception.Message)" }; Start-Sleep -Seconds (5 * $attempt) }
            }
            $hash = (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ne $knownNotices[$key]) { throw "$url has SHA-256 $hash, expected $($knownNotices[$key])" }
            $copied += "licenses/$name (from $url, SHA-256 $hash)"
        }
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
    '"Native files in the package" lists the native files of each runtime pack; the single-file',
    'BrowserFlip.exe embeds those the application needs.',
    ''
) -join "`n"
$text = $header + "`n" + ($sections -join "`n`n") + "`n"
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'THIRD-PARTY-NOTICES.txt'), $text, (New-Object Text.UTF8Encoding($false)))
Write-Host $text
