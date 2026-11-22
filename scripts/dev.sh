#!/usr/bin/env bash
# BrowserFlip cross-platform development entry point (macOS / Linux / Git Bash).
# The PowerShell equivalent is scripts/dev.ps1 — both expose the same commands.
#
#   ./scripts/dev.sh info          Show the detected OS, SDK and what would run
#   ./scripts/dev.sh restore       Restore the projects supported on this OS
#   ./scripts/dev.sh build         Build the projects supported on this OS
#   ./scripts/dev.sh test          Run the tests applicable on this OS
#   ./scripts/dev.sh check         restore + build + test (+ format on Windows)
#   ./scripts/dev.sh format        Verify formatting (Windows only; needs the full solution)
#   ./scripts/dev.sh sync-start    Safe start-of-session Git sync
#   ./scripts/dev.sh sync-finish   Test, review, commit and push
#
# Windows runs the full WPF build and test suite. macOS/Linux build and test the
# portable Core (net10.0) only; the WPF projects are reported as UNSUPPORTED.
# A green macOS run is NEVER a substitute for Windows verification.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

SOLUTION="BrowserFlip.slnx"
XPLAT_SOLUTION="BrowserFlip.CrossPlatform.slnx"
CONFIGURATION="${CONFIGURATION:-Release}"
RESULTS_DIR="$REPO_ROOT/TestResults"

export DOTNET_NOLOGO=true
export DOTNET_CLI_TELEMETRY_OPTOUT=true
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
# Keep tool output parseable regardless of the developer's locale.
export DOTNET_CLI_UI_LANGUAGE=en

# ---------------------------------------------------------------- environment

case "$(uname -s)" in
  Darwin) HOST_OS=macOS ;;
  Linux)  HOST_OS=Linux ;;
  MINGW*|MSYS*|CYGWIN*) HOST_OS=Windows ;;
  *)      HOST_OS="$(uname -s)" ;;
esac

if [ "$HOST_OS" = "Windows" ]; then IS_WINDOWS=1; else IS_WINDOWS=0; fi

find_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then command -v dotnet; return 0; fi
  for candidate in "$HOME/.dotnet/dotnet" /usr/local/share/dotnet/dotnet /usr/share/dotnet/dotnet /opt/homebrew/bin/dotnet; do
    [ -x "$candidate" ] && { printf '%s\n' "$candidate"; return 0; }
  done
  return 1
}

DOTNET="$(find_dotnet)" || {
  echo "ERROR: the .NET SDK was not found." >&2
  echo "       Install it from https://dotnet.microsoft.com/download (see global.json for the" >&2
  echo "       required version), or add its directory to PATH." >&2
  exit 127
}
# Make child processes (and nested dotnet invocations) see the same SDK.
PATH="$(dirname "$DOTNET"):$PATH"
export PATH

# ------------------------------------------------------------------ reporting

REPORT=()
FAILED=0
# Set when a command deliberately stopped without finishing (for example sync-finish
# leaving changes uncommitted). That is not a FAIL row, but it must not exit 0 either.
STOPPED=0

record() { # record <status> <step> [detail]
  REPORT+=("$1|$2|${3:-}")
  [ "$1" = "FAIL" ] && FAILED=1
  return 0
}

say() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }

print_report() {
  local status step detail colour
  [ "${#REPORT[@]}" -eq 0 ] && return 0
  printf '\n'
  printf '%s\n' "-------------------------------------------------------------------------------"
  printf ' BrowserFlip development report — host: %s, configuration: %s\n' "$HOST_OS" "$CONFIGURATION"
  printf '%s\n' "-------------------------------------------------------------------------------"
  for line in "${REPORT[@]}"; do
    status="${line%%|*}"
    step="${line#*|}"; step="${step%%|*}"
    detail="${line##*|}"
    case "$status" in
      PASS)        colour='\033[32m' ;;
      FAIL)        colour='\033[31m' ;;
      SKIPPED)     colour='\033[33m' ;;
      UNSUPPORTED) colour='\033[90m' ;;
      *)           colour='' ;;
    esac
    printf ' %b%-12s\033[0m %-42s %s\n' "$colour" "$status" "$step" "$detail"
  done
  printf '%s\n' "-------------------------------------------------------------------------------"
  # Only relevant when the report actually covers building or testing.
  if [ "$IS_WINDOWS" -eq 0 ] && printf '%s\n' "${REPORT[@]}" | grep -q '^UNSUPPORTED|'; then
    printf ' NOTE: this host cannot build or run the WPF application. A green run here does NOT\n'
    printf '       verify Windows behaviour — that requires a Windows machine or the Windows CI job.\n'
    printf '%s\n' "-------------------------------------------------------------------------------"
  fi
  if [ "$FAILED" -ne 0 ]; then
    printf ' RESULT: FAIL\n'
  elif [ "$STOPPED" -ne 0 ]; then
    printf ' RESULT: INCOMPLETE (nothing failed, but the command stopped early)\n'
  else
    printf ' RESULT: PASS\n'
  fi
  printf '%s\n\n' "-------------------------------------------------------------------------------"
}

# Parses "Passed!  - Failed: 0, Passed: 95, Skipped: 4, Total: 99" out of a test log.
test_counts() {
  local log="$1"
  grep -Eo 'Failed: *[0-9]+, *Passed: *[0-9]+, *Skipped: *[0-9]+, *Total: *[0-9]+' "$log" 2>/dev/null \
    | awk -F'[:,]' '{f+=$2; p+=$4; s+=$6; t+=$8} END {if (NR) printf "passed=%d failed=%d skipped=%d total=%d", p, f, s, t}'
}

# --------------------------------------------------------------------- targets

# What this host can work on.
if [ "$IS_WINDOWS" -eq 1 ]; then
  BUILD_TARGET="$SOLUTION"
  BUILD_ARGS=""                 # all target frameworks
  BUILD_LABEL="Build (full solution, WPF included)"
  TEST_LABEL="Test (Core + App, all target frameworks)"
else
  BUILD_TARGET="$XPLAT_SOLUTION"
  BUILD_ARGS="-f net10.0"       # the portable target only
  BUILD_LABEL="Build (portable Core, net10.0)"
  TEST_LABEL="Test (portable Core, net10.0)"
fi

# ------------------------------------------------------------------- commands

cmd_info() {
  printf 'Repository    : %s\n' "$REPO_ROOT"
  printf 'Host OS       : %s (%s)\n' "$HOST_OS" "$(uname -m)"
  printf 'dotnet        : %s\n' "$DOTNET"
  printf 'SDK version   : %s\n' "$("$DOTNET" --version 2>/dev/null)"
  printf 'Configuration : %s\n' "$CONFIGURATION"
  printf 'Build target  : %s %s\n' "$BUILD_TARGET" "$BUILD_ARGS"
  printf 'Git branch    : %s\n' "$(git rev-parse --abbrev-ref HEAD 2>/dev/null)"
  printf '\nOn this host:\n'
  if [ "$IS_WINDOWS" -eq 1 ]; then
    printf '  BrowserFlip.Core        build + test (net10.0 and net10.0-windows)\n'
    printf '  BrowserFlip.App (WPF)   build + test\n'
  else
    printf '  BrowserFlip.Core        build + test (net10.0)\n'
    printf '  BrowserFlip.App (WPF)   UNSUPPORTED — needs Windows\n'
    printf '  Native Windows API tests are not compiled into the net10.0 target.\n'
  fi
}

cmd_restore() {
  say "Restore ($BUILD_TARGET)"
  if "$DOTNET" restore "$BUILD_TARGET"; then
    record PASS "Restore"
  else
    record FAIL "Restore"
  fi
}

cmd_build() {
  say "$BUILD_LABEL"
  # shellcheck disable=SC2086 # BUILD_ARGS is a deliberate, space-separated argument list.
  if "$DOTNET" build "$BUILD_TARGET" -c "$CONFIGURATION" $BUILD_ARGS; then
    record PASS "$BUILD_LABEL"
  else
    record FAIL "$BUILD_LABEL"
  fi
  if [ "$IS_WINDOWS" -eq 0 ]; then
    record UNSUPPORTED "Build (BrowserFlip.App, WPF)" "requires Windows (NETSDK1100)"
  fi
}

cmd_test() {
  say "$TEST_LABEL"
  mkdir -p "$RESULTS_DIR"
  local log="$RESULTS_DIR/test-$(printf '%s' "$HOST_OS" | tr 'A-Z' 'a-z').log"
  local rc counts
  # shellcheck disable=SC2086 # BUILD_ARGS is a deliberate, space-separated argument list.
  "$DOTNET" test "$BUILD_TARGET" -c "$CONFIGURATION" $BUILD_ARGS \
    --logger trx --results-directory "$RESULTS_DIR" 2>&1 | tee "$log"
  rc=${PIPESTATUS[0]}
  counts="$(test_counts "$log")"
  if [ "$rc" -eq 0 ]; then
    record PASS "$TEST_LABEL" "${counts:-see $log}"
  else
    record FAIL "$TEST_LABEL" "${counts:-see $log}"
  fi
  if [ "$IS_WINDOWS" -eq 0 ]; then
    record UNSUPPORTED "Test (BrowserFlip.App.Tests, WPF)" "requires Windows"
    record SKIPPED "Test (Windows native API smoke tests)" "not compiled for net10.0"
  fi
}

cmd_format() {
  if [ "$IS_WINDOWS" -eq 0 ]; then
    record UNSUPPORTED "Format check" "runs on Windows (needs the WPF projects)"
    return 0
  fi
  say "Format check"
  if "$DOTNET" format whitespace "$SOLUTION" --verify-no-changes --verbosity diagnostic \
     && "$DOTNET" format style "$SOLUTION" --verify-no-changes --no-restore --severity warn --verbosity diagnostic; then
    record PASS "Format check"
  else
    record FAIL "Format check"
  fi
}

cmd_check() {
  cmd_restore
  [ "$FAILED" -eq 0 ] && cmd_build
  [ "$FAILED" -eq 0 ] && cmd_test
  cmd_format
  return 0
}

# ------------------------------------------------------------- git sync (safe)
#
# Rules these commands never break:
#   * never overwrite local modifications      * never force-push
#   * never reset --hard                       * never drop a stash
#   * never delete another branch or worktree
# Anything that would need one of those is reported and left for you to resolve.

cmd_sync_start() {
  local branch upstream ahead behind dirty=0
  branch="$(git rev-parse --abbrev-ref HEAD)"
  say "1/5 Working tree status (branch: $branch)"
  git status --short --branch
  [ -n "$(git status --porcelain)" ] && dirty=1

  say "2/5 Fetching origin (prune)"
  if git fetch --prune origin; then
    record PASS "Fetch origin"
  else
    record FAIL "Fetch origin"
    print_report
    return 1
  fi

  upstream="$(git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>/dev/null)"
  say "3/5 Branch relationship"
  if [ -z "$upstream" ]; then
    printf 'Branch %s has no upstream. Publish it with:\n  git push -u origin %s\n' "$branch" "$branch"
    record SKIPPED "Sync with upstream" "no upstream for $branch"
  else
    ahead="$(git rev-list --count "$upstream..HEAD")"
    behind="$(git rev-list --count "HEAD..$upstream")"
    printf 'Upstream: %s — ahead %s, behind %s\n' "$upstream" "$ahead" "$behind"

    say "4/5 Safe sync"
    if [ "$behind" -eq 0 ]; then
      record PASS "Sync with upstream" "already up to date (ahead $ahead)"
    elif [ "$dirty" -eq 1 ]; then
      printf 'Refusing to update: the working tree is dirty and %s commit(s) are waiting.\n' "$behind"
      printf 'Commit or `git stash push` your work first, then run sync-start again.\n'
      record FAIL "Sync with upstream" "dirty tree, $behind commit(s) behind"
    elif [ "$ahead" -eq 0 ]; then
      if git merge --ff-only "$upstream"; then
        record PASS "Sync with upstream" "fast-forwarded $behind commit(s)"
      else
        record FAIL "Sync with upstream" "fast-forward failed"
      fi
    else
      printf 'Diverged: %s local commit(s) and %s remote commit(s).\n' "$ahead" "$behind"
      printf 'Resolve it yourself — e.g. `git rebase %s` or `git merge %s`.\n' "$upstream" "$upstream"
      printf 'This script never rebases, merges non-fast-forward, or resets for you.\n'
      record FAIL "Sync with upstream" "diverged: ahead $ahead, behind $behind"
    fi
  fi

  say "5/5 Unpushed work and stashes"
  if [ -z "$upstream" ]; then
    record SKIPPED "Unpushed commits" "unknown: $branch is not on origin yet"
  elif [ "$(git rev-list --count "$upstream..HEAD")" != "0" ]; then
    git log --oneline "$upstream..HEAD"
    record SKIPPED "Unpushed commits" "$(git rev-list --count "$upstream..HEAD") commit(s) not on origin"
  else
    record PASS "Unpushed commits" "none"
  fi
  if [ -n "$(git stash list)" ]; then
    git stash list
    record SKIPPED "Stashes" "$(git stash list | wc -l | tr -d ' ') entry(ies) left untouched"
  fi
  if [ "$dirty" -eq 1 ]; then
    record SKIPPED "Local changes" "left untouched"
  fi

  print_report
  [ "$FAILED" -eq 0 ]
}

cmd_sync_finish() {
  local branch upstream message="${SYNC_MESSAGE:-}"
  branch="$(git rev-parse --abbrev-ref HEAD)"

  say "1/4 Tests"
  cmd_test
  if [ "$FAILED" -ne 0 ]; then
    printf '\nTests failed — nothing was committed or pushed.\n'
    print_report
    return 1
  fi

  say "2/4 Changes"
  git status --short
  if [ -z "$(git status --porcelain)" ]; then
    record SKIPPED "Commit" "nothing to commit"
  else
    if [ -z "$message" ]; then
      printf '\nThere are uncommitted changes. Review them, then commit yourself, or re-run with:\n'
      printf '  SYNC_MESSAGE="type(scope): summary" ./scripts/dev.sh sync-finish\n'
      record SKIPPED "Commit" "no SYNC_MESSAGE given; changes left in the working tree"
      STOPPED=1
      print_report
      return 1
    fi
    say "3/4 Commit"
    git add -A
    if git commit -m "$message"; then
      record PASS "Commit" "$message"
    else
      record FAIL "Commit"
      print_report
      return 1
    fi
  fi

  say "4/4 Push"
  upstream="$(git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>/dev/null)"
  if [ -z "$upstream" ]; then
    if git push -u origin "$branch"; then
      record PASS "Push" "published $branch"
    else
      record FAIL "Push" "see output above"
    fi
  elif git push; then
    record PASS "Push" "$upstream"
  else
    printf '\nPush was rejected. The remote has commits you do not have.\n'
    printf 'Run `./scripts/dev.sh sync-start` and integrate them. Never force-push a shared branch.\n'
    record FAIL "Push" "rejected (non-fast-forward)"
  fi

  if [ "$FAILED" -eq 0 ]; then
    git fetch --quiet origin
    if [ "$(git rev-parse HEAD)" = "$(git rev-parse '@{u}' 2>/dev/null)" ]; then
      record PASS "Remote in sync" "$(git rev-parse --short HEAD)"
    else
      record FAIL "Remote in sync" "origin does not match HEAD"
    fi
  fi

  print_report
  [ "$FAILED" -eq 0 ]
}

# ----------------------------------------------------------------------- main

usage() { sed -n '2,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

case "${1:-check}" in
  info)         cmd_info ;;
  restore)      cmd_restore; print_report ;;
  build)        cmd_build;   print_report ;;
  test)         cmd_test;    print_report ;;
  format)       cmd_format;  print_report ;;
  check|all)    cmd_check;   print_report ;;
  sync-start)   cmd_sync_start  || STOPPED=1 ;;
  sync-finish)  cmd_sync_finish || STOPPED=1 ;;
  -h|--help|help)
    usage ;;
  *)
    echo "Unknown command: $1" >&2
    usage >&2
    exit 2 ;;
esac

[ "$FAILED" -eq 0 ] && [ "$STOPPED" -eq 0 ]
