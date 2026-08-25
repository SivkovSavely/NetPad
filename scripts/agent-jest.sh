#!/usr/bin/env bash
set -uo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
APP_DIR="$REPO_ROOT/src/Apps/NetPad.Apps.App/App"

LOG="$(mktemp -t netpad-agent-jest.XXXXXX.log)"
trap 'rm -f -- "$LOG"' EXIT

cd -- "$APP_DIR"

npm test -- --runInBand --colors=false "$@" >"$LOG" 2>&1
status=$?

if ((status == 0)); then
    filtered="$(
        awk '
        /^PASS / ||
        /^Test Suites:/ ||
        /^Tests:/ ||
        /^Snapshots:/ ||
        /^Time:/ ||
        /^Ran all test suites/ {
            print
        }
        ' "$LOG"
    )"
else
    filtered="$(
        awk '
        function is_summary(line) {
            return line ~ /^Test Suites:/ ||
                   line ~ /^Tests:/ ||
                   line ~ /^Snapshots:/ ||
                   line ~ /^Time:/ ||
                   line ~ /^Ran all test suites/
        }

        /^FAIL / {
            in_failure = 1
            print
            next
        }

        /^PASS / {
            in_failure = 0
            next
        }

        {
            if (is_summary($0)) {
                in_failure = 0
                print
                next
            }

            if (in_failure) {
                print
            }
        }
        ' "$LOG"
    )"
fi

if [[ -n "$filtered" ]]; then
    printf '%s\n' "$filtered"
elif ((status != 0)); then
    echo "Jest failed with exit code $status; no recognized diagnostics found."
    echo "Last 120 lines:"
    tail -n 120 "$LOG"
fi

exit "$status"
