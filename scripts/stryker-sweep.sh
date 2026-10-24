#!/usr/bin/env bash
# Usage: scripts/stryker-sweep.sh [stryker args...]
#
# Restores the solution, then runs `dotnet stryker` from src/ with the given
# arguments (paths in them are relative to src/).
#
# Exit: Stryker's own status, or 1 when the restore fails or Stryker logged
# that it could not analyse a project (it then leaves that project unmutated
# and still reports a score).
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root/src" || exit 1

dotnet restore Anichron.slnx || exit 1

log=$(mktemp "${TMPDIR:-/tmp}/stryker-sweep.XXXXXXXX")
trap 'rm -f "$log"' EXIT

dotnet stryker "$@" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}

if dropped=$(grep -oE 'Analysis of project [^ ]+ failed' "$log"); then
  while IFS= read -r line; do
    echo "::error::Stryker left a project unmutated: $line"
  done <<<"$dropped"
  exit 1
fi
exit "$status"
