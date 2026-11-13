#!/usr/bin/env bash
# Tests for mutation-merge-parts.sh. Run: scripts/mutation-merge-parts.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/mutation-merge-parts.sh"
fixtures="$here/fixtures/merge"
work=$(mktemp -d "${TMPDIR:-/tmp}/mutation-merge-parts-test.XXXXXXXX")
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

part() {
  mkdir -p "$work/parts/mutation-part-$1"
  cp "$2" "$work/parts/mutation-part-$1/mutation-report.json"
}

plan='[{"name":"Anichron.Core"},{"name":"Anichron.Worker"}]'

check 'an empty plan fails' 1 'No mutation target list' '' "$work/parts" "$work/out"

part Anichron.Core "$fixtures/core.json"
check 'a project without a report fails, naming it' 1 'No mutation report from: Anichron.Worker' \
  "$plan" "$work/parts" "$work/out"
[ ! -e "$work/out/mutation-report.json" ] && pass 'a refused merge writes no report' \
  || fail 'a refused merge writes no report' 'no file' 'a report'

part Anichron.Worker "$fixtures/worker.json"
check 'every report present merges' 0 '' "$plan" "$work/parts" "$work/out"
expected=$("$here/mutation-report.sh" merge "$fixtures/core.json" "$fixtures/worker.json")
[ "$(cat "$work/out/mutation-report.json")" = "$expected" ] && pass 'the merged report is the merge of the parts' \
  || fail 'the merged report is the merge of the parts' "$expected" "$(cat "$work/out/mutation-report.json")"
cmp -s "$here/mutation-viewer.html" "$work/out/mutation-report.html" && pass 'the viewer is copied beside it' \
  || fail 'the viewer is copied beside it' 'mutation-viewer.html' 'missing or different'

rm -rf "$work/out"
echo '{}' > "$work/parts/mutation-part-Anichron.Worker/mutation-report.json"
check 'a merge the report script refuses fails' 1 '' "$plan" "$work/parts" "$work/out"
[ ! -e "$work/out/mutation-report.json" ] && [ ! -e "$work/out/mutation-report.json.tmp" ] \
  && pass 'a refused merge leaves nothing behind' || fail 'a refused merge leaves nothing behind' 'no files' "$(ls "$work/out")"

echo
if [ "$failures" -gt 0 ]; then echo "$failures failure(s)"; exit 1; fi
echo 'all mutation-merge-parts tests passed'
