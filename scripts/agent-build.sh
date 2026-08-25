#!/usr/bin/env bash
set -uo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"

LOG="$(mktemp -t netpad-agent-build.XXXXXX.log)"
trap 'rm -f -- "$LOG"' EXIT

cd -- "$REPO_ROOT"

if (($# == 0)); then
    args=(src/NetPad.sln)
else
    args=("$@")
fi

dotnet build "${args[@]}" >"$LOG" 2>&1
status=$?

filtered="$(
    {
        rg ': error(?: [A-Z]+[0-9]+)?:' "$LOG" || true
        rg '^[[:space:]]*[0-9]+ (Warning|Error)\(s\)[[:space:]]*$' "$LOG" || true
    } | sort --unique
)"

if [[ -n "$filtered" ]]; then
    printf '%s\n' "$filtered"
elif ((status != 0)); then
    echo "Build failed with exit code $status; no recognized diagnostics found."
    echo "Last 80 lines:"
    tail -n 80 "$LOG"
fi

exit "$status"
