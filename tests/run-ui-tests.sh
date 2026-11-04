#!/usr/bin/env bash
# Runs the end-to-end UI tests unattended.
#
#   tests/run-ui-tests.sh            # both suites
#   tests/run-ui-tests.sh mcp        # C# tests over the in-app automation server (tests/ThIDE.UiTests)
#   tests/run-ui-tests.sh atspi      # black-box Python tests over AT-SPI (tests/e2e-linux, Linux only)
#   tests/run-ui-tests.sh atspi -k 3d -x   # extra args go to pytest / dotnet test
#   tests/run-ui-tests.sh quarantine # only the C# UI tests marked [Trait("Category", "Quarantine")]
#
# The mcp suite leaves quarantined tests out. With UI_RETRY=1 it re-runs the tests that failed once,
# by name; tests that pass on the re-run are reported as flaky (a ::warning:: line on CI) and do not
# fail the suite. A test that fails twice does.
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

PY="$(command -v python3 || command -v python)"

# dotnet test of the C# UI suite; $1 = TRX file name, the rest go to dotnet test.
dotnet_ui_tests() {
    local trx="$1"; shift
    local cmd=(dotnet test "$ROOT/tests/ThIDE.UiTests/ThIDE.UiTests.csproj" -c "$CONFIG" --no-build
               --logger "trx;LogFileName=$trx" --results-directory "$ART" "$@")
    if [[ "$(uname)" == "Linux" ]]; then
        THIDE_UI_TESTS=1 xvfb-run -a -s "-screen 0 1600x1000x24 -nolisten tcp" \
            dbus-run-session -- "${cmd[@]}" 2> >(grep -v -E 'dbus-daemon|SpiRegistry|discover_other|xdg-desktop-portal|fusermount' >&2)
    else
        THIDE_UI_TESTS=1 "${cmd[@]}"
    fi
}

run_mcp() {
    echo "== C# UI tests (in-app automation server)"
    rm -f "$ART/ui-tests.trx" "$ART/ui-tests-retry.trx"
    dotnet_ui_tests ui-tests.trx --filter "Category!=Quarantine" "$@" && return 0
    [[ "${UI_RETRY:-0}" == "1" ]] || return 1

    local filter
    "$PY" "$ROOT/tests/ci/trx-failures.py" --filter-out "$ART/retry-filter.txt" "$ART/ui-tests.trx" > /dev/null
    filter="$(cat "$ART/retry-filter.txt")"
    if [[ -z "$filter" ]]; then echo "== the suite failed without a failed test (build or host error); no retry"; return 1; fi
    echo "== re-running the failed UI tests once"
    if dotnet_ui_tests ui-tests-retry.trx --filter "$filter"; then
        "$PY" "$ROOT/tests/ci/trx-failures.py" "$ART/ui-tests.trx" | while read -r name; do
            echo "::warning title=Flaky UI test::$name failed, then passed on the re-run"
        done
        return 0
    fi
    "$PY" "$ROOT/tests/ci/trx-failures.py" --annotate --summary "UI tests that failed twice" "$ART/ui-tests-retry.trx"
    return 1
}

run_quarantine() {
    echo "== quarantined C# UI tests"
    dotnet_ui_tests ui-tests-quarantine.trx --filter "Category=Quarantine" "$@"
}

run_atspi() {
    if [[ "$(uname)" != "Linux" ]]; then echo "== AT-SPI suite skipped (Linux only)"; return 0; fi
    echo "== AT-SPI black-box tests"
    E2E_ARTIFACTS="$ART/e2e-linux" python3 -m pytest "$ROOT/tests/e2e-linux" -p no:cacheprovider -q \
        --junitxml="$ART/e2e-linux.xml" "$@"
}

case "$SUITE" in
    mcp)   run_mcp "$@" || status=1 ;;
    quarantine) run_quarantine "$@" || status=1 ;;
    atspi) run_atspi "$@" || status=1 ;;
    all)   run_mcp || status=1; run_atspi || status=1 ;;
    *)     echo "usage: $0 [all|mcp|atspi|quarantine] [extra test args]" >&2; exit 2 ;;
esac

echo "== artifacts: $ART"
exit $status
