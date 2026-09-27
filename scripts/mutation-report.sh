#!/usr/bin/env bash
# Post-processing for Stryker.NET JSON reports. See docs/mutation-testing.md.
# Needs bash and jq; no CI context required.
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
usage:
  mutation-report.sh badge <report.json>            shields.io endpoint payload
  mutation-report.sh compare <a.json> <b.json>      non-zero if detected counts differ
  mutation-report.sh tally <report.json>            {detected, total, score} as JSON
EOF
  exit 2
}

# Shared tally. Both subcommands go through this, so the "measured nothing"
# guard cannot be omitted on one path — which is how compare once let two
# collapsed runs agree with each other and pass.
#
# Ignored and CompileError mutants are excluded, matching Stryker's own
# arithmetic; timeouts count as detected. A detected count of 0 is a valid
# measurement of a bad suite. A detectable total of 0 is a broken run.
readonly TALLY='
  [.files[].mutants[].status] as $s
  | (($s | map(select(. == "Pending")) | length)) as $pending
  | (($s | map(select(. == "Killed" or . == "Timeout")) | length)) as $detected
  | (($s | map(select(. == "Survived" or . == "NoCoverage")) | length)) as $undetected
  | ($detected + $undetected) as $total
  | if $pending > 0 then
      "\($path) has \($pending) pending mutant(s) — the run did not finish, so any score would read better than reality\n" | halt_error(1)
    elif $total == 0 then
      "no detectable mutants in \($path) — refusing to report a number\n" | halt_error(1)
    else
      { detected: $detected, total: $total }
    end
'

tally_of() {
  [ -f "$1" ] || { printf 'report not found: %s\n' "$1" >&2; exit 1; }
  jq -e --arg path "$1" "$TALLY" "$1"
}

# Counts and score in one object, straight from TALLY. `badge` is built on top
# of this rather than beside it, so there is exactly one place that turns a
# tally into a percentage.
#
# No thresholds are read: this reports a measurement, not a verdict, so a report
# without them is still tallyable where `badge` would refuse.
cmd_tally() {
  [ $# -eq 1 ] || usage
  tally_of "$1" | jq -e '
    . + { score: (.detected * 10000 / .total | round / 100) }
  '
}

cmd_badge() {
  [ $# -eq 1 ] || usage
  local tally thresholds
  # The score arithmetic lives in cmd_tally and is borrowed here. An earlier
  # revision shared a jq fragment between the two instead, which forced this
  # program into double quotes and fourteen backslashes to save one line.
  tally=$(cmd_tally "$1")
  # Bands come from the report's own thresholds, which Stryker copies from
  # stryker-config.json — so the badge cannot drift from the configured gate.
  # Both keys are required: jq treats `x >= null` as true, so a partial
  # thresholds object would silently green the badge.
  thresholds=$(jq -e --arg path "$1" '
    if (.thresholds.high | type) != "number" or (.thresholds.low | type) != "number" then
      "\($path) has no usable thresholds.high/.low — cannot choose a badge colour\n" | halt_error(1)
    else .thresholds end
  ' "$1")
  jq -n --argjson tally "$tally" --argjson t "$thresholds" '
    $tally.score as $score
    | {
        schemaVersion: 1,
        label: "mutation",
        message: (($score | tostring) + "%"),
        color: (if $score >= $t.high then "brightgreen"
                elif $score >= $t.low then "yellow"
                else "red" end)
      }
  '
}

# Guards the upstream defect that under-reports kills at concurrency above 1.
# It keeps killed-plus-survived constant while moving the split, so any single
# run looks self-consistent — comparing detected counts is what discriminates.
cmd_compare() {
  [ $# -eq 2 ] || usage
  local a b
  a=$(tally_of "$1" | jq -e '.detected')
  b=$(tally_of "$2" | jq -e '.detected')
  if [ "$a" -eq "$b" ]; then
    printf 'detected counts agree: %s\n' "$a"
    return 0
  fi
  printf 'detected counts diverge: %s (%s) vs %s (%s)\n' "$a" "$1" "$b" "$2" >&2
  printf 'The mutation measurement cannot be trusted; see docs/mutation-testing.md.\n' >&2
  return 1
}

case "${1:-}" in
  badge) shift; cmd_badge "$@" ;;
  compare) shift; cmd_compare "$@" ;;
  tally) shift; cmd_tally "$@" ;;
  *) usage ;;
esac
