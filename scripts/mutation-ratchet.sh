#!/usr/bin/env bash
# The mutation-score ratchet. See docs/mutation-testing.md, "The ratchet".
#
# Deliberately NOT part of mutation-report.sh: that script post-processes a
# report and needs no CI context, while this one is gate policy over stored
# state. Folding them together would give one file two reasons to change. The
# score still comes from exactly one place — this calls `mutation-report.sh
# tally` rather than re-deriving it.
#
# Needs bash and jq.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
report_tool="$here/mutation-report.sh"

usage() {
  cat >&2 <<'EOF'
usage:
  mutation-ratchet.sh thresholds <config.json> [mark.json]
      Stryker flags for the ratcheted gate, or nothing if there is no mark.

  mutation-ratchet.sh advance <config.json> <report.json> [mark.json]
      The new mark as JSON if the report beats the stored one.
      exit 3  the score did not beat the mark — the ordinary outcome
      exit 4  the stored mark exists but is unusable; refusing to overwrite it
EOF
  exit 2
}

# How far below the mark the gate sits. A build that loses a few mutants to
# ordinary churn should not fail; one that loses a chunk should.
readonly SLACK=5

# The committed thresholds. These are the floor the derived gate is taken
# against, never merely a starting point — see cmd_thresholds.
config_threshold() {
  jq -e --arg path "$1" --arg key "$2" '
    .["stryker-config"].thresholds[$key]
    | if type != "number" then
        "\($path) has no numeric thresholds.\($key)\n" | halt_error(1)
      else . end
  ' "$1"
}

# Reads .mark from the stored state. The exit status separates three cases,
# and keeping them separate is load-bearing — the two callers must not treat
# them alike:
#
#   0  a usable mark, printed on stdout
#   1  no state at all — the ordinary case before the first advance, and the
#      state left behind by deleting the file to reset the ratchet by hand
#   2  state exists but is unusable (corruption, or a hand-edit gone wrong)
#
# `thresholds` may fold 1 and 2 together: the worst outcome there is one run
# gated at the committed floor, and a floor is never weaker than what is in the
# source tree. Wedging every merge over one bad file on an orphan branch would
# be worse.
#
# `advance` must NOT fold them. Seeding from the floor on a corrupt file emits a
# mark derived from the floor rather than from the real stored value — and
# because the badges branch is force-pushed, that silently REWRITES a higher
# mark downwards. One corrupt byte plus one default-branch build would undo the
# ratchet, which is precisely what this feature promises cannot happen.
read_mark() {
  local state="${1:-}"
  [ -n "$state" ] && [ -f "$state" ] || return 1
  jq -e -r 'if (.mark | type) == "number" then .mark else empty end' "$state" 2>/dev/null || return 2
}

cmd_thresholds() {
  [ $# -ge 1 ] && [ $# -le 2 ] || usage
  local config="$1" state="${2:-}" mark status cfg_break cfg_high cfg_low brk low high

  mark=$(read_mark "$state") && status=0 || status=$?
  if [ "$status" -eq 2 ]; then
    printf 'ignoring unusable high-water mark in %s — falling back to the committed thresholds\n' \
      "$state" >&2
  fi
  # Acceptance criterion 6. Emitting NO flags is the fallback, rather than
  # echoing the static numbers back: that keeps stryker-config.json the single
  # place the committed thresholds are written down, so this script cannot
  # drift from it.
  [ "$status" -eq 0 ] || return 0

  cfg_break=$(config_threshold "$config" break)
  cfg_high=$(config_threshold "$config" high)
  # `low` is read too, not assumed equal to `break`. They happen to be equal
  # today, but flooring the derived low at `break` alone would let it drop
  # BELOW its own committed value the moment someone raised low above break —
  # quietly weakening a threshold while the code claimed to be a ratchet.
  cfg_low=$(config_threshold "$config" low)

  # break = max(committed break, floor(mark - SLACK)).
  #
  # The max() is not in the ticket, and without it the feature regresses on its
  # first run: the real mark is 46.58, so mark - 5 floors to 41, under the
  # committed 42. Acceptance criterion 4 read literally would LOWER the gate,
  # which is what the issue's opening line rules out. Floor rather than round
  # because rounding up can only cause a spurious failure.
  #
  # Each of the three is the max of its own committed value and the derived
  # one, so no threshold can ever come out below what the source tree asks for.
  #
  # Stryker validates break <= low <= high before it mutates anything and exits
  # 1 if that fails, which reads as a broken tool rather than a threshold event
  # (Q15). Q15 only names break <= low, but the same trap sits on the other
  # side: once the mark clears high + SLACK, a derived low overtakes the
  # committed high of 80. Raising high with it keeps the chain valid at every
  # mark; the max() keeps it from ever being dragged DOWN, which would quietly
  # widen the "good" band.
  #
  # The chain holds by construction rather than by assertion: Stryker already
  # requires cfgBreak <= cfgLow <= cfgHigh in the committed file, and taking the
  # max of each against the SAME derived number preserves that ordering.
  read -r brk low high < <(jq -n -r \
    --argjson mark "$mark" --argjson cfgBreak "$cfg_break" --argjson cfgLow "$cfg_low" \
    --argjson cfgHigh "$cfg_high" --argjson slack "$SLACK" '
      ($mark - $slack | floor) as $d
      | ([$d, $cfgBreak] | max) as $brk
      | ([$d, $cfgLow] | max) as $low
      | "\($brk) \($low) \([$cfgHigh, $low] | max)"
    ')

  printf -- '--break-at %s --threshold-low %s --threshold-high %s\n' "$brk" "$low" "$high"
}

cmd_advance() {
  [ $# -ge 2 ] && [ $# -le 3 ] || usage
  local config="$1" report="$2" state="${3:-}" mark status score

  # Goes through `tally`, so every guard it owns applies here too: a run with
  # pending mutants or nothing detectable cannot record a mark. That matters
  # more here than anywhere else, because a mark is permanent — an interrupted
  # sweep scores better than reality, and writing that number down raises the
  # bar forever on a measurement that never happened.
  score=$("$report_tool" tally "$report" | jq -e -r '.score')

  mark=$(read_mark "$state") && status=0 || status=$?
  case "$status" in
    0) ;;
    # No stored mark at all: seed from the committed break so the first
    # default-branch build records a real score instead of refusing forever.
    1) mark=$(config_threshold "$config" break) ;;
    # A mark that exists but cannot be read is NOT the same as no mark, and
    # seeding from the floor here would be destructive rather than merely
    # degraded: the emitted mark gets force-pushed over the stored one, so a
    # single corrupt byte would rewrite a high mark down to whatever this run
    # happened to score. Refuse, and let a human look at the file — the only
    # cost is that the mark does not move on this build.
    *)
      printf 'refusing to advance: the high-water mark in %s exists but is unusable.\n' "$state" >&2
      printf 'Seeding from the committed floor would overwrite it with a lower number.\n' >&2
      printf 'See docs/mutation-testing.md, "Lowering the mark by hand".\n' >&2
      return 4
      ;;
  esac

  if ! jq -n -e --argjson a "$score" --argjson b "$mark" '$a > $b' >/dev/null; then
    printf '%s%% is not above the high-water mark of %s%% — leaving it alone\n' \
      "$score" "$mark" >&2
    return 3
  fi

  jq -n \
    --argjson mark "$score" \
    --arg recorded "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    --arg commit "${GITHUB_SHA:-$(git rev-parse HEAD 2>/dev/null || echo unknown)}" \
    '{ mark: $mark, recorded: $recorded, commit: $commit }'
}

case "${1:-}" in
  thresholds) shift; cmd_thresholds "$@" ;;
  advance) shift; cmd_advance "$@" ;;
  *) usage ;;
esac
