#!/usr/bin/env bash
# Tests for mutation-stage-part.sh. Run: scripts/mutation-stage-part.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/mutation-stage-part.sh"
fixtures="$here/fixtures/merge"
work=$(mktemp -d "${TMPDIR:-/tmp}/mutation-stage-part-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

# check <name> <expected exit> <expected output fragment> <args...>
check() {
  local name="$1" code="$2" expected="$3"; shift 3
  local out status
  out=$("$script" "$@" 2>&1); status=$?
  if [ "$status" -ne "$code" ]; then
    fail "$name" "exit $code" "exit $status with: $out"
  elif [[ "$out" != *"$expected"* ]]; then
    fail "$name" "output containing '$expected'" "$out"
  else
    pass "$name"
  fi
}

check 'a missing argument fails with the usage' 1 'usage:' "$work/reports" "$work/parts"
check 'an empty name fails with the usage' 1 'usage:' "$work/reports" '' "$work/parts"

check 'a run without a report stages nothing' 0 '' "$work/reports" Anichron.Core "$work/parts"
[ ! -e "$work/parts" ] && pass 'and creates no folder for the upload' \
  || fail 'and creates no folder for the upload' 'no parts folder' "$(find "$work/parts")"

mkdir -p "$work/reports"
cp "$fixtures/core.json" "$work/reports/mutation-report.json"
echo 'not a report' > "$work/reports/mutation-report.md"
check 'a report is staged' 0 '' "$work/reports" Anichron.Core "$work/parts"
cmp -s "$fixtures/core.json" "$work/parts/mutation-part-Anichron.Core/mutation-report.json" \
  && pass 'as the project part, unchanged' || fail 'as the project part, unchanged' 'core.json' 'missing or different'
[ "$(ls "$work/parts/mutation-part-Anichron.Core")" = 'mutation-report.json' ] \
  && pass 'and nothing else from the reports' || fail 'and nothing else from the reports' 'mutation-report.json' "$(ls "$work/parts/mutation-part-Anichron.Core")"

merged=$("$here/mutation-merge-parts.sh" '[{"name":"Anichron.Core"}]' "$work/parts" "$work/out" 2>&1); status=$?
[ "$status" -eq 0 ] && [ -f "$work/out/mutation-report.json" ] && pass 'the merge reads what was staged' \
  || fail 'the merge reads what was staged' 'exit 0 and a merged report' "exit $status with: $merged"

echo
if [ "$failures" -gt 0 ]; then echo "$failures failure(s)"; exit 1; fi
echo 'all mutation-stage-part tests passed'
