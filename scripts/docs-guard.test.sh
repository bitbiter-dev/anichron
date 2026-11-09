#!/usr/bin/env bash
# Tests for docs-guard.sh. Run: scripts/docs-guard.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/docs-guard.sh"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

git -C "$work" init -q
mkdir -p "$work/docs/agents" "$work/.claude/docs"

words() { local i out=""; for ((i = 0; i < $1; i++)); do out+="w "; done; printf '%s' "$out"; }

# doc <path>: stdin is the Markdown.
doc() { cat >"$work/$1"; }

assert_passes() {
  local label="$1" out status
  out=$(cd "$work" && "$script" 2>&1); status=$?
  if [ "$status" -eq 0 ]; then pass "$label"; else fail "$label" 'exit 0' "exit $status with: $out"; fi
}

assert_fails() {
  local label="$1" needle="$2" out status
  out=$(cd "$work" && "$script" 2>&1); status=$?
  if [ "$status" -eq 1 ] && [[ "$out" == *"$needle"* ]]; then
    pass "$label"
  else
    fail "$label" "exit 1 with output containing '$needle'" "exit $status with: $out"
  fi
}

reset() { rm -f "$work"/docs/*.md "$work"/docs/agents/*.md "$work"/.claude/docs/*.md; : >"$work/CLAUDE.md"; }

reset
doc docs/a.md <<EOF
# Title

$(words 60)

- $(words 60)
- $(words 30)
  $(words 30)

| a | b |
|---|---|
| $(words 60) | $(words 60) |
EOF
assert_passes "paragraphs, list items and cells of 60 words pass"

reset
doc docs/a.md <<EOF
$(words 40)
$(words 21)
EOF
assert_fails "a paragraph over 60 words across lines fails" "docs/a.md:1: 61 words"

reset
doc docs/a.md <<EOF
intro

- $(words 10)
  $(words 51)
EOF
assert_fails "a list item with its continuation lines fails" "docs/a.md:3: 61 words"

reset
doc docs/a.md <<EOF
| a |
|---|
| $(words 61) |
EOF
assert_fails "a table cell over 60 words fails" "docs/a.md:3: table cell of 61 words"

reset
doc docs/a.md <<EOF
\`\`\`
$(words 200)
\`\`\`
EOF
assert_passes "code blocks are skipped"

reset
doc docs/a.md <<EOF
- $(words 40)
- $(words 40)
EOF
assert_passes "each list item counts on its own"

reset
doc .claude/docs/b.md <<EOF
$(words 61)
EOF
assert_fails ".claude/docs is checked too" ".claude/docs/b.md:1: 61 words"

reset
doc CLAUDE.md <<EOF
$(words 61)
EOF
assert_fails "CLAUDE.md is checked too" "CLAUDE.md:1: 61 words"

reset
doc docs/agents/c.md <<EOF
$(words 61)
EOF
assert_passes "docs/agents, written by the skills' setup, is skipped"

if [ "$failures" -gt 0 ]; then
  printf '%d failure(s)\n' "$failures"
  exit 1
fi
printf 'all passed\n'
