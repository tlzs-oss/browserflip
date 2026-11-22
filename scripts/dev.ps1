<#
.SYNOPSIS
    BrowserFlip cross-platform development entry point (PowerShell).

.DESCRIPTION
    The PowerShell twin of scripts/dev.sh; both expose the same commands and the same report.

        ./scripts/dev.ps1 info          Show the detected OS, SDK and what would run
        ./scripts/dev.ps1 restore       Restore the projects supported on this OS
        ./scripts/dev.ps1 build         Build the projects supported on this OS
        ./scripts/dev.ps1 test          Run the tests applicable on this OS
        ./scripts/dev.ps1 check         restore + build + test (+ format on Windows)
        ./scripts/dev.ps1 format        Verify formatting (Windows only)
        ./scripts/dev.ps1 sync-start    Safe start-of-session Git sync
        ./scripts/dev.ps1 sync-finish   Test, review, commit and push

    Windows runs the full WPF build and test suite. macOS/Linux build and test the portable
    Core (net10.0) only; the WPF projects are reported as UNSUPPORTED. A green non-Windows run
    is NEVER a substitute for Windows verification.

.PARAMETER Command
    One of the commands listed above. Defaults to 'check'.

.PARAMETER Configuration
    MSBuild configuration. Defaults to Release.

.PARAMETER Message
    Commit message used by 'sync-finish'. Without it, uncommitted changes are left alone.

.EXAMPLE
    ./scripts/dev.ps1 check

.EXAMPLE
    ./scripts/dev.ps1 sync-finish -Message "feat(core): add X"
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('info', 'restore', 'build', 'test', 'format', 'check', 'all', 'sync-start', 'sync-finish', 'help')]
    [string] $Command = 'check',

    [string] $Configuration = 'Release',

    [string] $Message
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# PowerShell 7.4+ turns a non-zero native exit code into a terminating error when
# $ErrorActionPreference is 'Stop'. This script inspects $LASTEXITCODE itself and reports
# PASS/FAIL, so that behaviour is switched off.
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "PowerShell 7 or later is required (found $($PSVersionTable.PSVersion)). On Windows, use 'pwsh', or run scripts/dev.sh from Git Bash."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$solution = 'BrowserFlip.slnx'
$xplatSolution = 'BrowserFlip.CrossPlatform.slnx'
$resultsDir = Join-Path $repoRoot 'TestResults'

$env:DOTNET_NOLOGO = 'true'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 'true'
# Keep tool output parseable regardless of the developer's locale.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

# ---------------------------------------------------------------- environment

$hostOs = if ($IsWindows) { 'Windows' } elseif ($IsMacOS) { 'macOS' } elseif ($IsLinux) { 'Linux' } else { 'Unknown' }
$onWindows = $hostOs -eq 'Windows'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $exeName = if ($onWindows) { 'dotnet.exe' } else { 'dotnet' }
    $roots = @((Join-Path $HOME '.dotnet'), '/usr/local/share/dotnet', '/usr/share/dotnet')
    if ($env:ProgramFiles) { $roots += (Join-Path $env:ProgramFiles 'dotnet') }
    $candidates = @($roots | Where-Object { $_ -and (Test-Path (Join-Path $_ $exeName)) })

    if ($candidates.Count -eq 0) {
        throw "The .NET SDK was not found. Install it from https://dotnet.microsoft.com/download (see global.json for the required version), or add its directory to PATH."
    }
    $env:PATH = "$($candidates[0])$([IO.Path]::PathSeparator)$env:PATH"
}

if ($onWindows) {
    $buildTarget = $solution
    $buildArgs = @()
    $buildLabel = 'Build (full solution, WPF included)'
    $testLabel = 'Test (Core + App, all target frameworks)'
} else {
    $buildTarget = $xplatSolution
    $buildArgs = @('-f', 'net10.0')
    $buildLabel = 'Build (portable Core, net10.0)'
    $testLabel = 'Test (portable Core, net10.0)'
}

# ------------------------------------------------------------------ reporting

$script:report = [Collections.Generic.List[object]]::new()
$script:failed = $false

function Add-Result {
    param(
        [ValidateSet('PASS', 'FAIL', 'SKIPPED', 'UNSUPPORTED')] [string] $Status,
        [string] $Step,
        [string] $Detail = ''
    )
    $script:report.Add([pscustomobject]@{ Status = $Status; Step = $Step; Detail = $Detail })
    if ($Status -eq 'FAIL') { $script:failed = $true }
}

function Write-Step { param([string] $Text) Write-Host "`n==> $Text" -ForegroundColor Cyan }

function Write-Report {
    if ($script:report.Count -eq 0) { return }
    $rule = '-' * 79
    Write-Host ''
    Write-Host $rule
    Write-Host " BrowserFlip development report - host: $hostOs, configuration: $Configuration"
    Write-Host $rule
    foreach ($row in $script:report) {
        $colour = switch ($row.Status) {
            'PASS' { 'Green' }
            'FAIL' { 'Red' }
            'SKIPPED' { 'Yellow' }
            default { 'DarkGray' }
        }
        Write-Host (' {0,-12}' -f $row.Status) -ForegroundColor $colour -NoNewline
        Write-Host (' {0,-42} {1}' -f $row.Step, $row.Detail)
    }
    Write-Host $rule
    # Only relevant when the report actually covers building or testing.
    if ((-not $onWindows) -and ($script:report | Where-Object { $_.Status -eq 'UNSUPPORTED' })) {
        Write-Host ' NOTE: this host cannot build or run the WPF application. A green run here does NOT'
        Write-Host '       verify Windows behaviour - that requires a Windows machine or the Windows CI job.'
        Write-Host $rule
    }
    Write-Host (' RESULT: ' + $(if ($script:failed) { 'FAIL' } else { 'PASS' })) -ForegroundColor $(if ($script:failed) { 'Red' } else { 'Green' })
    Write-Host "$rule`n"
}

# Parses "Failed: 0, Passed: 95, Skipped: 4, Total: 99" out of a test log.
function Get-TestCounts {
    param([string[]] $Lines)
    $totals = @{ passed = 0; failed = 0; skipped = 0; total = 0 }
    $any = $false
    foreach ($line in $Lines) {
        if ($line -match 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)') {
            $any = $true
            $totals.failed += [int]$Matches[1]
            $totals.passed += [int]$Matches[2]
            $totals.skipped += [int]$Matches[3]
            $totals.total += [int]$Matches[4]
        }
    }
    if (-not $any) { return '' }
    "passed=$($totals.passed) failed=$($totals.failed) skipped=$($totals.skipped) total=$($totals.total)"
}

# ------------------------------------------------------------------- commands

function Invoke-Info {
    Write-Host "Repository    : $repoRoot"
    Write-Host "Host OS       : $hostOs ($([Runtime.InteropServices.RuntimeInformation]::OSArchitecture))"
    Write-Host "dotnet        : $((Get-Command dotnet).Source)"
    Write-Host "SDK version   : $(dotnet --version)"
    Write-Host "Configuration : $Configuration"
    Write-Host "Build target  : $buildTarget $($buildArgs -join ' ')"
    Write-Host "Git branch    : $(git rev-parse --abbrev-ref HEAD)"
    Write-Host ''
    Write-Host 'On this host:'
    if ($onWindows) {
        Write-Host '  BrowserFlip.Core        build + test (net10.0 and net10.0-windows)'
        Write-Host '  BrowserFlip.App (WPF)   build + test'
    } else {
        Write-Host '  BrowserFlip.Core        build + test (net10.0)'
        Write-Host '  BrowserFlip.App (WPF)   UNSUPPORTED - needs Windows'
        Write-Host '  Native Windows API tests are not compiled into the net10.0 target.'
    }
}

function Invoke-Restore {
    Write-Step "Restore ($buildTarget)"
    dotnet restore $buildTarget
    if ($LASTEXITCODE -eq 0) { Add-Result PASS 'Restore' } else { Add-Result FAIL 'Restore' }
}

function Invoke-Build {
    Write-Step $buildLabel
    dotnet build $buildTarget -c $Configuration @buildArgs
    if ($LASTEXITCODE -eq 0) { Add-Result PASS $buildLabel } else { Add-Result FAIL $buildLabel }
    if (-not $onWindows) {
        Add-Result UNSUPPORTED 'Build (BrowserFlip.App, WPF)' 'requires Windows (NETSDK1100)'
    }
}

function Invoke-Test {
    Write-Step $testLabel
    New-Item -ItemType Directory -Force $resultsDir | Out-Null
    $log = Join-Path $resultsDir "test-$($hostOs.ToLowerInvariant()).log"
    dotnet test $buildTarget -c $Configuration @buildArgs --logger trx --results-directory $resultsDir |
        Tee-Object -FilePath $log
    $exit = $LASTEXITCODE
    $counts = Get-TestCounts (@(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue))
    if (-not $counts) { $counts = "see $log" }
    if ($exit -eq 0) { Add-Result PASS $testLabel $counts } else { Add-Result FAIL $testLabel $counts }
    if (-not $onWindows) {
        Add-Result UNSUPPORTED 'Test (BrowserFlip.App.Tests, WPF)' 'requires Windows'
        Add-Result SKIPPED 'Test (Windows native API smoke tests)' 'not compiled for net10.0'
    }
}

function Invoke-Format {
    if (-not $onWindows) {
        Add-Result UNSUPPORTED 'Format check' 'runs on Windows (needs the WPF projects)'
        return
    }
    Write-Step 'Format check'
    dotnet format whitespace $solution --verify-no-changes --verbosity diagnostic
    $ok = $LASTEXITCODE -eq 0
    if ($ok) {
        dotnet format style $solution --verify-no-changes --no-restore --severity warn --verbosity diagnostic
        $ok = $LASTEXITCODE -eq 0
    }
    if ($ok) { Add-Result PASS 'Format check' } else { Add-Result FAIL 'Format check' }
}

function Invoke-Check {
    Invoke-Restore
    if (-not $script:failed) { Invoke-Build }
    if (-not $script:failed) { Invoke-Test }
    Invoke-Format
}

# ------------------------------------------------------------- git sync (safe)
#
# Rules these commands never break:
#   * never overwrite local modifications      * never force-push
#   * never reset --hard                       * never drop a stash
#   * never delete another branch or worktree
# Anything that would need one of those is reported and left for you to resolve.

function Get-Upstream {
    $u = git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return $u
}

function Invoke-SyncStart {
    $branch = git rev-parse --abbrev-ref HEAD
    Write-Step "1/5 Working tree status (branch: $branch)"
    git status --short --branch
    $dirty = [bool](git status --porcelain)

    Write-Step '2/5 Fetching origin (prune)'
    git fetch --prune origin
    if ($LASTEXITCODE -ne 0) { Add-Result FAIL 'Fetch origin'; Write-Report; return }
    Add-Result PASS 'Fetch origin'

    $upstream = Get-Upstream
    Write-Step '3/5 Branch relationship'
    if (-not $upstream) {
        Write-Host "Branch $branch has no upstream. Publish it with:`n  git push -u origin $branch"
        Add-Result SKIPPED 'Sync with upstream' "no upstream for $branch"
    } else {
        $ahead = [int](git rev-list --count "$upstream..HEAD")
        $behind = [int](git rev-list --count "HEAD..$upstream")
        Write-Host "Upstream: $upstream - ahead $ahead, behind $behind"

        Write-Step '4/5 Safe sync'
        if ($behind -eq 0) {
            Add-Result PASS 'Sync with upstream' "already up to date (ahead $ahead)"
        } elseif ($dirty) {
            Write-Host "Refusing to update: the working tree is dirty and $behind commit(s) are waiting."
            Write-Host 'Commit or `git stash push` your work first, then run sync-start again.'
            Add-Result FAIL 'Sync with upstream' "dirty tree, $behind commit(s) behind"
        } elseif ($ahead -eq 0) {
            git merge --ff-only $upstream
            if ($LASTEXITCODE -eq 0) {
                Add-Result PASS 'Sync with upstream' "fast-forwarded $behind commit(s)"
            } else {
                Add-Result FAIL 'Sync with upstream' 'fast-forward failed'
            }
        } else {
            Write-Host "Diverged: $ahead local commit(s) and $behind remote commit(s)."
            Write-Host "Resolve it yourself - e.g. ``git rebase $upstream`` or ``git merge $upstream``."
            Write-Host 'This script never rebases, merges non-fast-forward, or resets for you.'
            Add-Result FAIL 'Sync with upstream' "diverged: ahead $ahead, behind $behind"
        }
    }

    Write-Step '5/5 Unpushed work and stashes'
    $upstream = Get-Upstream
    if (-not $upstream) {
        Add-Result SKIPPED 'Unpushed commits' "unknown: $branch is not on origin yet"
    } else {
        $unpushed = [int](git rev-list --count "$upstream..HEAD")
        if ($unpushed -gt 0) {
            git log --oneline "$upstream..HEAD"
            Add-Result SKIPPED 'Unpushed commits' "$unpushed commit(s) not on origin"
        } else {
            Add-Result PASS 'Unpushed commits' 'none'
        }
    }
    $stashes = @(git stash list)
    if ($stashes.Count -gt 0) {
        $stashes | Write-Host
        Add-Result SKIPPED 'Stashes' "$($stashes.Count) entry(ies) left untouched"
    }
    if ($dirty) { Add-Result SKIPPED 'Local changes' 'left untouched' }

    Write-Report
}

function Invoke-SyncFinish {
    $branch = git rev-parse --abbrev-ref HEAD

    Write-Step '1/4 Tests'
    Invoke-Test
    if ($script:failed) {
        Write-Host "`nTests failed - nothing was committed or pushed."
        Write-Report
        return
    }

    Write-Step '2/4 Changes'
    git status --short
    if (-not (git status --porcelain)) {
        Add-Result SKIPPED 'Commit' 'nothing to commit'
    } elseif (-not $Message) {
        Write-Host "`nThere are uncommitted changes. Review them, then commit yourself, or re-run with:"
        Write-Host '  ./scripts/dev.ps1 sync-finish -Message "type(scope): summary"'
        Add-Result SKIPPED 'Commit' 'no -Message given; changes left in the working tree'
        Write-Report
        return
    } else {
        Write-Step '3/4 Commit'
        git add -A
        git commit -m $Message
        if ($LASTEXITCODE -ne 0) { Add-Result FAIL 'Commit'; Write-Report; return }
        Add-Result PASS 'Commit' $Message
    }

    Write-Step '4/4 Push'
    if (-not (Get-Upstream)) {
        git push -u origin $branch
        if ($LASTEXITCODE -eq 0) { Add-Result PASS 'Push' "published $branch" } else { Add-Result FAIL 'Push' 'see output above' }
    } else {
        git push
        if ($LASTEXITCODE -eq 0) {
            Add-Result PASS 'Push' (Get-Upstream)
        } else {
            Write-Host "`nPush was rejected. The remote has commits you do not have."
            Write-Host 'Run `./scripts/dev.ps1 sync-start` and integrate them. Never force-push a shared branch.'
            Add-Result FAIL 'Push' 'rejected (non-fast-forward)'
        }
    }

    if (-not $script:failed) {
        git fetch --quiet origin
        if ((git rev-parse HEAD) -eq (git rev-parse '@{u}')) {
            Add-Result PASS 'Remote in sync' (git rev-parse --short HEAD)
        } else {
            Add-Result FAIL 'Remote in sync' 'origin does not match HEAD'
        }
    }

    Write-Report
}

# ----------------------------------------------------------------------- main

switch ($Command) {
    'info' { Invoke-Info }
    'help' { Get-Help $PSCommandPath -Detailed }
    'restore' { Invoke-Restore; Write-Report }
    'build' { Invoke-Build; Write-Report }
    'test' { Invoke-Test; Write-Report }
    'format' { Invoke-Format; Write-Report }
    { $_ -in 'check', 'all' } { Invoke-Check; Write-Report }
    'sync-start' { Invoke-SyncStart }
    'sync-finish' { Invoke-SyncFinish }
}

if ($script:failed) { exit 1 }
