#!/usr/bin/env bash
# Tests for changed-areas.sh. Run: scripts/changed-areas.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/changed-areas.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

# assert_areas <label> <expected-output> <event> [changed-path ...]
assert_areas() {
  local label="$1" expected="$2" event="$3"; shift 3
  local out status
  out=$(printf '%s\n' "$@" | sed '/^$/d' | "$script" "$event" 2>&1); status=$?
  if [ "$status" -eq 0 ] && [ "$out" = "$expected" ]; then
    pass "$label"
  else
    fail "$label" "exit 0 with: $expected" "exit $status with: $out"
  fi
}

echo "pull requests"

assert_areas 'a change only under the UI leaves the backend unchanged' 'backend=false' \
  pull_request src/Anichron.UI/src/app/router.tsx src/Anichron.UI/package.json
assert_areas 'a backend change marks the backend changed' 'backend=true' \
  pull_request src/Anichron.API/Program.cs
assert_areas 'a change to both marks the backend changed' 'backend=true' \
  pull_request src/Anichron.UI/package.json src/Anichron.Core/AnichronDbContext.cs
assert_areas 'a shared file outside both areas marks the backend changed' 'backend=true' \
  pull_request src/Anichron.UI/package.json .github/workflows/ci.yml
assert_areas 'a path that only starts like the UI folder is not the UI' 'backend=true' \
  pull_request src/Anichron.UI.Tests/Example.cs
assert_areas 'a rename out of the UI lists its new path and marks the backend changed' 'backend=true' \
  pull_request src/Anichron.UI/scripts/check.sh scripts/check.sh
assert_areas 'deleting a UI file leaves the backend unchanged' 'backend=false' \
  pull_request src/Anichron.UI/src/features/placeholder/PlaceholderPage.tsx
assert_areas 'deleting a backend file marks the backend changed' 'backend=true' \
  pull_request src/Anichron.Worker/Crawling/Removed.cs
assert_areas 'a quoted path git could not print plainly marks the backend changed' 'backend=true' \
  pull_request '"src/Anichron.UI/caf\303\251.ts"'
assert_areas 'a pull request with no changed path marks the backend changed' 'backend=true' \
  pull_request

echo "other events"

assert_areas 'a push marks the backend changed whatever its paths' 'backend=true' \
  push src/Anichron.UI/package.json
assert_areas 'a manual run marks the backend changed' 'backend=true' \
  workflow_dispatch

echo "misconfiguration"

out=$("$script" 2>&1 </dev/null); status=$?
if [ "$status" -eq 2 ] && [[ "$out" == *usage* ]]; then pass 'a missing event name is refused'; else fail 'a missing event name is refused' 'exit 2 with usage' "exit $status with: $out"; fi

echo
if [ "$failures" -eq 0 ]; then
  echo 'all changed-areas tests passed'
else
  echo "$failures changed-areas test(s) failed"
  exit 1
fi
