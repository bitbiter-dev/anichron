#!/usr/bin/env bash
# Fixture-driven tests for mutation-report.sh. Run: scripts/mutation-report.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/mutation-report.sh"
fixtures="$here/fixtures"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

assert_eq() {
  if [ "$2" = "$3" ]; then pass "$1"; else fail "$1" "$2" "$3"; fi
}

assert_contains() {
  case "$3" in
    *"$2"*) pass "$1" ;;
    *) fail "$1" "output containing '$2'" "$3" ;;
  esac
}

echo "badge"

out=$("$script" badge "$fixtures/passing.json")
assert_eq "reports the score for 3 killed of 4 detectable" '75' "$(echo "$out" | jq -r '.message' | tr -d '%')"
assert_eq "75 sits between low 42 and high 80, so yellow" 'yellow' "$(echo "$out" | jq -r '.color')"
assert_eq "emits the shields.io endpoint schema version" '1' "$(echo "$out" | jq -r '.schemaVersion')"
assert_eq "labels the badge" 'mutation' "$(echo "$out" | jq -r '.label')"

out=$("$script" badge "$fixtures/below-break.json")
assert_eq "1 killed of 10 detectable is 10%" '10' "$(echo "$out" | jq -r '.message' | tr -d '%')"
assert_eq "10 is under low 42, so red" 'red' "$(echo "$out" | jq -r '.color')"

out=$("$script" badge "$fixtures/above-high.json")
assert_eq "timeouts count as detected; ignored and compile errors do not" '90' "$(echo "$out" | jq -r '.message' | tr -d '%')"
assert_eq "90 is at or over high 80, so brightgreen" 'brightgreen' "$(echo "$out" | jq -r '.color')"

# Band edges are inclusive at the bottom of each band. Asserted because the
# script's own >= comparisons are exactly what a mutation would flip.
out=$("$script" badge "$fixtures/exactly-high.json")
assert_eq "a score exactly at high is 80%" '80' "$(echo "$out" | jq -r '.message' | tr -d '%')"
assert_eq "exactly high counts as brightgreen, not yellow" 'brightgreen' "$(echo "$out" | jq -r '.color')"

out=$("$script" badge "$fixtures/exactly-low.json")
assert_eq "a score exactly at that report's low is 40%" '40' "$(echo "$out" | jq -r '.message' | tr -d '%')"
assert_eq "exactly low counts as yellow, not red" 'yellow' "$(echo "$out" | jq -r '.color')"

echo
echo "compare"

if out=$("$script" compare "$fixtures/divergent-a.json" "$fixtures/divergent-a.json" 2>&1); then
  pass "identical reports agree, so exit 0"
else
  fail "identical reports agree, so exit 0" 'exit 0' "exit $? — $out"
fi

# divergent-a and divergent-b both have 10 mutants with Killed+Survived = 10;
# only the split differs (8/2 vs 5/5). That is the shape of the upstream defect.
if out=$("$script" compare "$fixtures/divergent-a.json" "$fixtures/divergent-b.json" 2>&1); then
  fail "a moved kill split is caught even though the total is unchanged" 'non-zero exit' "exit 0 — $out"
else
  pass "a moved kill split is caught even though the total is unchanged"
fi
assert_contains "names the first detected count" '8' "$out"
assert_contains "names the second detected count" '5' "$out"

echo
echo "failing loudly"

# Asserts a non-zero exit AND that the message explains which failure it was.
# Exit status alone is too weak: a usage error also exits non-zero, so every
# one of these would pass against a script that simply rejected its arguments.
assert_fails() {
  local name="$1" expected="$2"; shift 2
  local out
  if out=$("$@" 2>&1); then
    fail "$name" "non-zero exit mentioning '$expected'" "exit 0 — $out"
    return
  fi
  case "$out" in
    *"$expected"*) pass "$name" ;;
    *) fail "$name" "message containing '$expected'" "$out" ;;
  esac
}

assert_fails "a missing report is named, not silently scored" 'report not found' "$script" badge "$fixtures/does-not-exist.json"
assert_fails "malformed JSON is reported as a parse failure" 'parse error' "$script" badge "$fixtures/malformed.json"
assert_fails "an undetectable report is explained in operator terms" 'no detectable mutants' "$script" badge "$fixtures/nothing-detectable.json"
assert_fails "compare names a missing report" 'report not found' "$script" compare "$fixtures/passing.json" "$fixtures/does-not-exist.json"
assert_fails "compare reports malformed JSON as a parse failure" 'parse error' "$script" compare "$fixtures/passing.json" "$fixtures/malformed.json"
# Two runs that both measured nothing would otherwise "agree" and pass the gate —
# the most likely way a compromised measurement looks healthy.
assert_fails "compare refuses two reports that detected nothing" 'no detectable mutants' "$script" compare "$fixtures/nothing-detectable.json" "$fixtures/nothing-detectable.json"
assert_fails "compare refuses one report that detected nothing" 'no detectable mutants' "$script" compare "$fixtures/passing.json" "$fixtures/nothing-detectable.json"
assert_fails "no subcommand prints usage" 'usage:' "$script"
assert_fails "badge with no report path prints usage" 'usage:' "$script" badge
assert_fails "badge with too many paths prints usage" 'usage:' "$script" badge "$fixtures/passing.json" "$fixtures/passing.json"
assert_fails "compare with only one report prints usage" 'usage:' "$script" compare "$fixtures/passing.json"
assert_fails "a report with no thresholds cannot be coloured" 'no usable thresholds' "$script" badge "$fixtures/no-thresholds.json"
assert_fails "a partial thresholds object cannot be coloured" 'no usable thresholds' "$script" badge "$fixtures/partial-thresholds.json"
# Pending mutants are dropped from the denominator, so an interrupted run would
# score better than reality — 2 killed of 2 completed reads as 100%.
assert_fails "an unfinished run is refused rather than flattered" 'did not finish' "$script" badge "$fixtures/unfinished.json"
assert_fails "compare refuses an unfinished run" 'did not finish' "$script" compare "$fixtures/passing.json" "$fixtures/unfinished.json"

echo
if [ "$failures" -gt 0 ]; then
  printf '%d failure(s)\n' "$failures"
  exit 1
fi
echo 'all tests passed'
