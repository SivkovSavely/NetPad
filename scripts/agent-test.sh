#!/usr/bin/env bash
set -uo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"

LOG="$(mktemp -t netpad-agent-test.XXXXXX.log)"
trap 'rm -f -- "$LOG"' EXIT

cd -- "$REPO_ROOT"

if (($# == 0)); then
    args=(src/NetPad.sln)
else
    args=("$@")
fi

dotnet test "${args[@]}" >"$LOG" 2>&1
status=$?

filtered="$(
    awk '
    function is_summary(line) {
        return line ~ /^(Passed|Failed)!/ ||
               line ~ /^[[:space:]]*[0-9]+ (Warning|Error)\(s\)[[:space:]]*$/
    }

    function is_compile_error(line) {
        return line ~ /: error ([A-Z]+[0-9]+)?:/
    }

    {
        line = $0

        if (is_compile_error(line)) {
            print line
            next
        }

        if (is_summary(line)) {
            print line
            section = ""
            next
        }

        if (line ~ /^  Failed /) {
            print line
            section = "failure"
            next
        }

        if (line ~ /^  Error Message:/) {
            print line
            section = "error"
            next
        }

        if (line ~ /^  Stack Trace:/) {
            print line
            section = "stack"
            next
        }

        if (section == "error") {
            if (line ~ /^  Stack Trace:/) {
                print line
                section = "stack"
                next
            }

            if (line ~ /^  Failed / || is_summary(line)) {
                print line
                section = line ~ /^  Failed / ? "failure" : ""
                next
            }

            print line
            next
        }

        if (section == "stack") {
            if (line ~ /^  Failed /) {
                print line
                section = "failure"
                next
            }

            if (is_summary(line)) {
                print line
                section = ""
                next
            }

            if (line ~ /^[[:space:]]+at / ||
                line ~ /^[[:space:]]+---/ ||
                line ~ /^[[:space:]]+-----/ ||
                line ~ /^[[:space:]]*$/) {
                print line
            }

            next
        }
    }
    ' "$LOG"
)"

if [[ -n "$filtered" ]]; then
    printf '%s\n' "$filtered"
elif ((status != 0)); then
    echo "Tests failed with exit code $status; no recognized diagnostics found."
    echo "Last 120 lines:"
    tail -n 120 "$LOG"
fi

exit "$status"
