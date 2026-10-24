#!/usr/bin/env bash
# Runs the end-to-end UI tests unattended.
#
#   tests/run-ui-tests.sh            # both suites
#   tests/run-ui-tests.sh mcp        # C# tests over the in-app automation server (tests/ThIDE.UiTests)
#   tests/run-ui-tests.sh atspi      # black-box Python tests over AT-SPI (tests/e2e-linux, Linux only)
#   tests/run-ui-tests.sh atspi -k 3d -x   # extra args go to pytest / dotnet test
#
# On Linux everything runs on a private Xvfb display with its own D-Bus session and a throw-away
# profile, so it neither needs nor touches a desktop. Results land in tests/ui-artifacts/
# (TRX for the C# suite, JUnit XML plus failure screenshots/trees/logs for the AT-SPI suite).
# Exit code is non-zero if any suite fails.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SUITE="${1:-all}"
[[ $# -gt 0 ]] && shift
ART="${UI_ARTIFACTS:-$ROOT/tests/ui-artifacts}"
CONFIG="${CONFIGURATION:-Debug}"
mkdir -p "$ART"
status=0

echo "== building ThIDE + UI tests ($CONFIG)"
dotnet build "$ROOT/tests/ThIDE.UiTests/ThIDE.UiTests.csproj" -m:1 -c "$CONFIG" \
    -p:TherionAutoIncrementBuild=false -nologo -v:q || exit 1

run_mcp() {
    echo "== C# UI tests (in-app automation server)"
    local cmd=(dotnet test "$ROOT/tests/ThIDE.UiTests/ThIDE.UiTests.csproj" -c "$CONFIG" --no-build
               --logger "trx;LogFileName=ui-tests.trx" --results-directory "$ART" "$@")
    if [[ "$(uname)" == "Linux" ]]; then
        THIDE_UI_TESTS=1 xvfb-run -a -s "-screen 0 1600x1000x24 -nolisten tcp" \
            dbus-run-session -- "${cmd[@]}" 2> >(grep -v -E 'dbus-daemon|SpiRegistry|discover_other|xdg-desktop-portal|fusermount' >&2)
    else
        THIDE_UI_TESTS=1 "${cmd[@]}"
    fi
}

run_atspi() {
    if [[ "$(uname)" != "Linux" ]]; then echo "== AT-SPI suite skipped (Linux only)"; return 0; fi
    echo "== AT-SPI black-box tests"
    E2E_ARTIFACTS="$ART/e2e-linux" python3 -m pytest "$ROOT/tests/e2e-linux" -p no:cacheprovider -q \
        --junitxml="$ART/e2e-linux.xml" "$@"
}

case "$SUITE" in
    mcp)   run_mcp "$@" || status=1 ;;
    atspi) run_atspi "$@" || status=1 ;;
    all)   run_mcp || status=1; run_atspi || status=1 ;;
    *)     echo "usage: $0 [all|mcp|atspi] [extra test args]" >&2; exit 2 ;;
esac

echo "== artifacts: $ART"
exit $status
