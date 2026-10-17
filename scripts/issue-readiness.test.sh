#!/usr/bin/env bash
# Tests for issue-readiness.sh. Run: scripts/issue-readiness.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/issue-readiness.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

# issue <number> <labels-csv> <body> [comment ...]
issue() {
  local number="$1" labels="$2" body="$3"
  shift 3
  jq -n --argjson number "$number" --arg labels "$labels" --arg body "$body" \
    '{number: $number, labels: ($labels | split(",") | map(select(. != "") | {name: .})), body: $body,
      comments: ($ARGS.positional | map({body: .}))}' --args "$@"
}

check() {
  local label="$1" want_status="$2" needle="$3" json="$4" out status
  out=$("$script" <<<"$json" 2>&1); status=$?
  if [ "$status" -ne "$want_status" ]; then
    fail "$label" "exit $want_status" "exit $status with: $out"
  elif [ -n "$needle" ] && [[ "$out" != *"$needle"* ]]; then
    fail "$label" "output containing '$needle'" "$out"
  elif [ -z "$needle" ] && [ -n "$out" ]; then
    fail "$label" 'no output' "$out"
  else
    pass "$label"
  fi
}

complete=$'## What to build\nA thing.\n\n## First caller\nThe On This Day endpoint (#38).\n\n## Design\nDesign: not needed because it reuses the existing repository pattern.'

echo 'issue-readiness.sh'
check 'a complete issue is ready and silent' 0 '' "$(issue 300 ready-for-agent "$complete")"
check 'a link to the design note counts' 0 '' \
  "$(issue 300 ready-for-agent $'## First caller\nCI.\n\n## Design\nhttps://github.com/o/r/issues/300#issuecomment-1')"
check 'a missing first caller is named' 1 'First caller' \
  "$(issue 300 ready-for-agent $'## What to build\nA thing.\n\n## Design\nDesign: not needed because it is docs only.')"
check 'an empty first caller section is missing' 1 'First caller' \
  "$(issue 300 ready-for-agent $'## First caller\n\n## Design\nDesign: not needed because it is docs only.')"
check 'a missing design part is named' 1 'Design' \
  "$(issue 300 ready-for-agent $'## First caller\nCI.')"
check '"not needed" without a reason is missing' 1 'Design' \
  "$(issue 300 ready-for-agent $'## First caller\nCI.\n\n## Design\nDesign: not needed because …')"
check 'both parts missing names both' 1 'Design' \
  "$(issue 300 ready-for-agent 'Just a body.')"
check 'parts given only in an agent brief comment count' 0 '' \
  "$(issue 300 ready-for-agent 'Original report.' $'## Agent Brief\n\n**Summary:** x\n**First caller:** the story player\n**Design:** Design: not needed because it is a bug fix in place.')"
check 'the natural brief form "**Design:** not needed because" counts' 0 '' \
  "$(issue 300 ready-for-agent 'Original report.' $'## Agent Brief\n\n**First caller:** the story player\n**Design:** not needed because it is a bug fix in place.')"
check 'headings match regardless of case and a trailing colon' 0 '' \
  "$(issue 300 ready-for-agent $'## First Caller:\nCI.\n\n## DESIGN\nDesign: not needed because it is docs only.')"
check 'brief fields may be list items' 0 '' \
  "$(issue 300 ready-for-agent 'Report.' $'## Agent Brief\n\n- **First caller:** CI\n- **Design:** not needed because it is docs only.')"
check 'a bare issue number is not a design note' 1 'Design' \
  "$(issue 300 ready-for-agent $'## First caller\nCI.\n\n## Design\nTBD, waiting on #250')"
check 'an ADR link counts as the design' 0 '' \
  "$(issue 300 ready-for-agent $'## First caller\nCI.\n\n## Design\nSee docs/adr/0006-design-before-implementation.md')"
check 'a comment that is not an agent brief does not count' 1 'First caller' \
  "$(issue 300 ready-for-agent 'Original report.' $'**First caller:** someone\n**Design:** Design: not needed because reasons given.')"
check 'a legacy issue (#261 or below) is skipped' 0 '' "$(issue 261 ready-for-agent 'Just a body.')"
check 'an issue without ready-for-agent is skipped' 0 '' "$(issue 300 needs-design 'Just a body.')"
check 'an epic is skipped' 0 '' "$(issue 300 'ready-for-agent,epic:1' 'Just a body.')"
check 'unreadable input is a usage error' 2 'expected the JSON' 'not json'

if [ "$failures" -gt 0 ]; then
  printf '\n%d failure(s)\n' "$failures"
  exit 1
fi
printf '\nall passed\n'
