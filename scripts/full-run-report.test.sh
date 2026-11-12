#!/usr/bin/env bash
# Tests for full-run-report.sh. Run: scripts/full-run-report.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/full-run-report.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/full-run-report-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

mkdir -p "$work/bin"
cat >"$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
echo "$*" >>"$GH_CALLS"
case "$1 $2" in
  'issue list') cat "$GH_OPEN" ;;
  'run view') cat "$GH_JOBS" ;;
  'issue create' | 'issue comment') cat >>"$GH_CALLS" ;;
esac
EOF
chmod +x "$work/bin/gh"

open_issues='[{"number":3,"title":"Full run failed: CI on master"},{"number":9,"title":"Full run failed: Weekly full run"}]'
red='{"url":"https://ci/42","jobs":[{"name":"Coverage","conclusion":"success"},{"name":"UI sweep","conclusion":"failure"},{"name":"Backend sweep","conclusion":"cancelled"},{"name":"Publish quality reports","conclusion":"skipped"},{"name":"Report the outcome","conclusion":""}]}'
green='{"url":"https://ci/42","jobs":[{"name":"Coverage","conclusion":"success"},{"name":"Publish quality reports","conclusion":"skipped"},{"name":"Report the outcome","conclusion":""}]}'

run() {
  : >"$work/calls"
  printf '%s' "$1" >"$work/open"
  printf '%s' "$2" >"$work/jobs"
  GH_CALLS="$work/calls" GH_OPEN="$work/open" GH_JOBS="$work/jobs" PATH="$work/bin:$PATH" \
    "$script" 'Weekly full run' 42 mira 2>&1
}

expect() {
  local label="$1" want_status="$2" needle="$3" out="$4" status="$5"
  if [ "$status" -eq "$want_status" ] && [[ "$(cat "$work/calls")" == *"$needle"* ]]; then pass "$label"; else fail "$label" "exit $want_status, gh call containing '$needle'" "exit $status: $out / $(cat "$work/calls")"; fi
}

echo 'full-run-report.sh'

out=$(run '[]' "$red"); status=$?
expect 'a failed run opens an assigned issue' 0 \
  "issue create --title Full run failed: Weekly full run --label ready-for-agent --milestone Engineering Process --assignee mira" "$out" "$status"
expect 'the issue lists the failed and cancelled jobs' 0 $'- `UI sweep`: failure\n- `Backend sweep`: cancelled' "$out" "$status"
expect 'the issue links the run' 0 'The full run https://ci/42 failed' "$out" "$status"
if grep -qE '`(Coverage|Publish quality reports|Report the outcome)`' "$work/calls"; then
  fail 'passed, skipped and running jobs are not listed' 'only the failed jobs' "$(cat "$work/calls")"
else
  pass 'passed, skipped and running jobs are not listed'
fi
expect 'the issue carries the readiness parts' 0 $'## First caller\n\nThe next run of Weekly full run. The PR with the fix closes this issue.' "$out" "$status"

out=$(run "$open_issues" "$red"); status=$?
expect 'an open issue gets a comment' 0 'issue comment 9 --body-file -' "$out" "$status"
[[ "$(cat "$work/calls")" == *'issue create'* ]] && fail 'an open issue is not duplicated' 'no issue create' "$(cat "$work/calls")"

out=$(run "$open_issues" "$green"); status=$?
expect 'a passing run closes its open issue' 0 'issue close 9 --comment Passed again: https://ci/42' "$out" "$status"
[[ "$(cat "$work/calls")" == *'issue close 3'* ]] && fail "another source's issue stays open" 'no close of #3' "$(cat "$work/calls")"

out=$(run '[]' "$green"); status=$?
if [ "$status" -eq 0 ] && [[ "$out" == 'passed: Weekly full run' ]] && ! grep -qE 'issue (create|close|comment)' "$work/calls"; then
  pass 'a passing run with no open issue does nothing'
else
  fail 'a passing run with no open issue does nothing' 'only the list call' "$out / $(cat "$work/calls")"
fi

: >"$work/calls"
out=$(GH_CALLS="$work/calls" PATH="$work/bin:$PATH" "$script" 'Weekly full run' 42 2>&1); status=$?
expect 'a missing argument is a usage error' 2 '' "$out" "$status"

if [ "$failures" -gt 0 ]; then
  printf '\n%d failure(s)\n' "$failures"
  exit 1
fi
printf '\nall passed\n'
