#!/usr/bin/env bash
# Tests for arch-review-due.sh. Run: scripts/arch-review-due.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/arch-review-due.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/arch-review-due-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

mkdir -p "$work/bin"
cat >"$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
echo "$*" >>"$GH_CALLS"
case "$1 $2" in
  'issue list') cat "$GH_OPEN" ;;
  'issue create') cat >/dev/null ;;
esac
EOF
chmod +x "$work/bin/gh"

# repo <commits-by-mira> <commits-by-dependabot> [tag]
repo() {
  local dir="$work/repo" i
  rm -rf "$dir"
  git init -q "$dir"
  git -C "$dir" -c user.name=mira -c user.email=mira@example.org commit -q --allow-empty -m base
  [ -n "${3:-}" ] && git -C "$dir" tag "$3"
  for ((i = 0; i < $1; i++)); do
    git -C "$dir" -c user.name=mira -c user.email=mira@example.org commit -q --allow-empty -m "change $i"
  done
  for ((i = 0; i < $2; i++)); do
    git -C "$dir" -c 'user.name=dependabot[bot]' -c user.email=bot@example.org commit -q --allow-empty -m "bump $i"
  done
}

run() {
  : >"$work/calls"
  printf '%s' "${1:-}" >"$work/open"
  (cd "$work/repo" && GH_CALLS="$work/calls" GH_OPEN="$work/open" PATH="$work/bin:$PATH" "$script" 3 2>&1)
}

expect() {
  local label="$1" want_status="$2" needle="$3" calls_needle="$4" out="$5" status="$6"
  if [ "$status" -ne "$want_status" ]; then
    fail "$label" "exit $want_status" "exit $status with: $out"
  elif [[ "$out" != *"$needle"* ]]; then
    fail "$label" "output containing '$needle'" "$out"
  elif [[ "$(cat "$work/calls")" != *"$calls_needle"* ]]; then
    fail "$label" "gh call containing '$calls_needle'" "$(cat "$work/calls")"
  else
    pass "$label"
  fi
}

echo 'arch-review-due.sh'

repo 2 0 arch-review/2026-10-16
out=$(run); status=$?
expect 'below the threshold opens nothing' 0 '2 PRs since arch-review/2026-10-16' '' "$out" "$status"
[ -s "$work/calls" ] && fail 'below the threshold calls no gh' 'no gh calls' "$(cat "$work/calls")"

repo 2 5 arch-review/2026-10-16
out=$(run); status=$?
expect 'Dependabot commits are not counted' 0 '2 PRs since' '' "$out" "$status"

repo 3 0 arch-review/2026-10-16
out=$(run); status=$?
expect 'at the threshold opens a labelled issue' 0 'opened' \
  'issue create --title Architecture review due (3 PRs since arch-review/2026-10-16) --label ready-for-agent' "$out" "$status"

repo 4 0 arch-review/2026-10-16
out=$(run 7); status=$?
expect 'above it with one open, the count is updated' 0 'updated #7' \
  'issue edit 7 --title Architecture review due (4 PRs since arch-review/2026-10-16)' "$out" "$status"
[[ "$(cat "$work/calls")" == *'issue create'* ]] && fail 'an open issue is not duplicated' 'no issue create' "$(cat "$work/calls")"

repo 1 0 arch-review/2026-01-01
git -C "$work/repo" tag arch-review/2026-10-16 HEAD~0
git -C "$work/repo" -c user.name=mira -c user.email=mira@example.org commit -q --allow-empty -m after
out=$(run); status=$?
expect 'counting starts at the newest tag' 0 '1 PRs since arch-review/2026-10-16' '' "$out" "$status"

repo 1 0
out=$(run); status=$?
expect 'no tag is a usage error' 2 'no arch-review/* tag' '' "$out" "$status"

if [ "$failures" -gt 0 ]; then
  printf '\n%d failure(s)\n' "$failures"
  exit 1
fi
printf '\nall passed\n'
