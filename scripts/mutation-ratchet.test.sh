#!/usr/bin/env bash
# Fixture-driven tests for mutation-ratchet.sh. Run: scripts/mutation-ratchet.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/mutation-ratchet.sh"
fixtures="$here/fixtures"
cfg="$fixtures/config-thresholds.json"
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

# Asserts the command fails AND says why. An exit-status-only assertion would
# pass on a script that died for an unrelated reason.
assert_fails() {
  local label="$1" needle="$2"; shift 2
  local out status
  out=$("$@" 2>&1); status=$?
  if [ "$status" -eq 0 ]; then
    fail "$label" "non-zero exit" "exit 0 with: $out"
  else
    assert_contains "$label" "$needle" "$out"
  fi
}

echo "thresholds — fallback when there is no state"

# Acceptance criterion 6: missing state falls back to the committed config. The
# script emits NO flags, which is how Stryker ends up using stryker-config.json
# — the fallback is "say nothing", not "compute the static numbers ourselves",
# so there is only one place the static thresholds are written down.
out=$("$script" thresholds "$cfg" "$fixtures/does-not-exist.json"); status=$?
assert_eq 'a missing mark emits no flags, so the config governs' '' "$out"
assert_eq 'and that is not an error' '0' "$status"

out=$("$script" thresholds "$cfg")
assert_eq 'omitting the state argument entirely does the same' '' "$out"

# Degrade, do not block. A corrupt state file on the badges branch must not
# wedge every merge — the gate still holds at the committed floor, which is
# never weaker than what is in the source tree.
out=$("$script" thresholds "$cfg" "$fixtures/highwater-malformed.json" 2>&1)
assert_contains 'a malformed mark is reported on stderr' 'ignoring' "$out"
out=$("$script" thresholds "$cfg" "$fixtures/highwater-malformed.json" 2>/dev/null)
assert_eq 'and falls back to the config rather than failing' '' "$out"

echo "thresholds — the ratchet never lowers the gate"

# THE case the ticket's arithmetic does not survive on its own. The first real
# mark is 46.58; minus five is 41.58, which floors to 41 — BELOW the committed
# break of 42. Read literally, acceptance criterion 4 would weaken the gate on
# its very first advance, contradicting the issue's own opening line, "never
# quietly slips back". The static threshold is a floor, not a starting point.
out=$("$script" thresholds "$cfg" "$fixtures/highwater-below-static.json")
assert_contains 'a mark whose derived value is under the static break holds at the static break' '--break-at 42' "$out"
assert_contains 'and low holds with it' '--threshold-low 42' "$out"

out=$("$script" thresholds "$cfg" "$fixtures/highwater-above-static.json")
assert_contains 'a mark of 62.5 derives a break of 57' '--break-at 57' "$out"

echo "thresholds — low and break move together (Q15)"

# Stryker rejects break > low before running a single mutant, which exits 1 and
# reads as a broken tool rather than a threshold event. These two assertions are
# the ones that make that unrepresentable: the values are equal by construction.
out=$("$script" thresholds "$cfg" "$fixtures/highwater-above-static.json")
brk=$(printf '%s' "$out" | sed -n 's/.*--break-at \([0-9]*\).*/\1/p')
low=$(printf '%s' "$out" | sed -n 's/.*--threshold-low \([0-9]*\).*/\1/p')
assert_eq 'break and low are emitted as the same number' "$brk" "$low"

echo "thresholds — the ratchet does not collide with threshold-high"

# The second thing the ticket does not anticipate. Stryker's input constraint is
# break <= low <= high, not merely break <= low. Config high is 80, so once the
# mark passes 85 the derived low overtakes it and Stryker refuses to start —
# the exact failure mode Q15 exists to prevent, arriving through the other side
# of the inequality. high has to rise with them.
out=$("$script" thresholds "$cfg" "$fixtures/highwater-near-high.json")
assert_contains 'a mark of 88 derives a break of 83' '--break-at 83' "$out"
assert_contains 'and drags threshold-high up to stay >= low' '--threshold-high 83' "$out"

high=$(printf '%s' "$out" | sed -n 's/.*--threshold-high \([0-9]*\).*/\1/p')
low=$(printf '%s' "$out" | sed -n 's/.*--threshold-low \([0-9]*\).*/\1/p')
if [ "$high" -ge "$low" ]; then pass 'break <= low <= high holds at the top of the range'
else fail 'break <= low <= high holds at the top of the range' "high >= $low" "high $high"; fi

# Below the collision point high must NOT be dragged down — that would quietly
# relax the "good" band every time the mark sat under 85.
out=$("$script" thresholds "$cfg" "$fixtures/highwater-above-static.json")
assert_contains 'a mark below the collision leaves high at the configured 80' '--threshold-high 80' "$out"

echo "advance — only on a higher score"

# passing.json is 75%, above the 46.58 mark, so it advances.
out=$("$script" advance "$cfg" "$fixtures/passing.json" "$fixtures/highwater-below-static.json")
assert_eq 'a higher score raises the mark to that score' '75' "$(printf '%s' "$out" | jq -r '.mark')"

# below-break.json is 10%, under the mark, so it must not.
assert_fails 'a lower score does not raise the mark' 'not above' \
  "$script" advance "$cfg" "$fixtures/below-break.json" "$fixtures/highwater-below-static.json"

# Equal is not above. Guards the >= / > comparison directly: passing.json is
# 75% and the fixture's mark is exactly 75.
assert_fails 'a score equal to the mark does not raise it' 'not above' \
  "$script" advance "$cfg" "$fixtures/passing.json" "$fixtures/highwater-at-75.json"

# With no state at all the static break is the starting mark, so the first
# default-branch build records a real number instead of refusing forever.
out=$("$script" advance "$cfg" "$fixtures/passing.json" "$fixtures/does-not-exist.json")
assert_eq 'with no stored state the static break seeds the mark' '75' "$(printf '%s' "$out" | jq -r '.mark')"

echo "advance — a corrupt mark is not the same as no mark"

# REGRESSION, found by both review axes independently. An earlier version
# collapsed "absent" and "unusable" in one reader, which is defensible for
# `thresholds` — one run at the committed floor — and destructive here. With a
# corrupt file the seed fell back to the committed break of 42, any score above
# 42 then "advanced", and because CI force-pushes the badges branch the emitted
# mark overwrote the stored one. A mark of 88 would have been rewritten to 75.
#
# Refusing costs only that the mark does not move on this build.
assert_fails 'a corrupt mark refuses rather than seeding from the floor' 'unusable' \
  "$script" advance "$cfg" "$fixtures/passing.json" "$fixtures/highwater-malformed.json"

out=$("$script" advance "$cfg" "$fixtures/passing.json" "$fixtures/highwater-malformed.json" 2>/dev/null); status=$?
assert_eq 'and emits nothing that could be published over the stored mark' '' "$out"
assert_eq 'with a status distinct from the ordinary "did not beat it"' '4' "$status"

# The contrast that makes the distinction real: absent still seeds.
out=$("$script" advance "$cfg" "$fixtures/passing.json" "$fixtures/does-not-exist.json")
assert_eq 'while a genuinely absent mark still seeds from the floor' '75' "$(printf '%s' "$out" | jq -r '.mark')"

echo "thresholds — no threshold is ever emitted below its committed value"

# thresholds.low was previously never read: the derived low was floored at
# `break`, so a config with low above break would have emitted a low BELOW its
# own committed value — weakening a threshold inside a ratchet.
out=$("$script" thresholds "$fixtures/config-low-above-break.json" "$fixtures/highwater-above-static.json")
assert_contains 'break holds at its own committed floor' '--break-at 57' "$out"
assert_contains 'and low holds at ITS committed floor, not at break' '--threshold-low 60' "$out"
assert_contains 'high still holds at its own' '--threshold-high 80' "$out"

# The chain Stryker validates must survive the separate floors.
brk=$(printf '%s' "$out" | sed -n 's/.*--break-at \([0-9]*\).*/\1/p')
low=$(printf '%s' "$out" | sed -n 's/.*--threshold-low \([0-9]*\).*/\1/p')
high=$(printf '%s' "$out" | sed -n 's/.*--threshold-high \([0-9]*\).*/\1/p')
if [ "$brk" -le "$low" ] && [ "$low" -le "$high" ]; then
  pass 'break <= low <= high still holds when the committed floors differ'
else
  fail 'break <= low <= high still holds when the committed floors differ' 'ordered' "$brk $low $high"
fi

echo "advance — refuses what tally refuses"

# The ratchet must not be a way around the pending-mutant guard: an interrupted
# sweep scores better than reality, and recording that permanently raises the
# bar on a number that was never measured.
assert_fails 'an interrupted run cannot advance the mark' 'pending' \
  "$script" advance "$cfg" "$fixtures/unfinished.json" "$fixtures/highwater-below-static.json"

assert_fails 'a run with nothing detectable cannot advance the mark' 'refusing' \
  "$script" advance "$cfg" "$fixtures/nothing-detectable.json" "$fixtures/highwater-below-static.json"

echo "advance — the recorded payload"

out=$("$script" advance "$cfg" "$fixtures/passing.json" "$fixtures/does-not-exist.json")
assert_contains 'records when the mark was set' '"recorded"' "$out"
assert_contains 'records which commit set it, for the recovery path' '"commit"' "$out"
assert_eq 'and is valid JSON' '0' "$(printf '%s' "$out" | jq -e . >/dev/null 2>&1; echo $?)"

echo "usage"

assert_fails 'an unknown subcommand is refused' 'usage' "$script" frobnicate
assert_fails 'thresholds needs a config' 'usage' "$script" thresholds
assert_fails 'advance needs a report' 'usage' "$script" advance "$cfg"

echo
if [ "$failures" -eq 0 ]; then
  echo "all mutation-ratchet.sh tests passed"
else
  printf '%s failing assertion(s)\n' "$failures"
  exit 1
fi
