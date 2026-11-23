#!/usr/bin/env bash
# Tests for land.sh. Run: scripts/land.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/land.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/land-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }
check() { if eval "$2"; then pass "$1"; else fail "$1" "$2" "${3:-false}"; fi; }

mkdir -p "$work/bin"
cat >"$work/bin/date" <<'EOF'
#!/usr/bin/env bash
if [ "$*" = +%s ] && [ -f "$GH_STATE/clock" ]; then cat "$GH_STATE/clock"; else exec /bin/date "$@"; fi
EOF
cat >"$work/bin/sleep" <<'EOF'
#!/usr/bin/env bash
if [ -f "$GH_STATE/sleep-jump" ] && [ -f "$GH_STATE/clock" ]; then
  echo $(($(cat "$GH_STATE/clock") + $(cat "$GH_STATE/sleep-jump"))) >"$GH_STATE/clock"
  rm "$GH_STATE/sleep-jump"
fi
exec /bin/sleep "$@"
EOF
cat >"$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
echo "$*" >>"$GH_STATE/calls"
advance() { [ ! -f "$GH_STATE/clock" ] || echo $(($(cat "$GH_STATE/clock") + $1)) >"$GH_STATE/clock"; }
# The main land.sh shell: the topmost land.sh process below with-lock.sh.
land_pid() {
  local p=$PPID top=''
  while [ "$p" -gt 1 ]; do
    case "$(ps -o args= -p "$p")" in *with-lock.sh*) break ;; *land.sh*) top=$p ;; esac
    p=$(ps -o ppid= -p "$p" | tr -d ' ')
  done
  echo "$top"
}
if [ -f "$GH_STATE/all-fail" ]; then
  advance 300
  echo 'error connecting to api.github.com' >&2
  exit 1
fi
case "$1 $2" in
  'pr list') [ -f "$GH_STATE/pr" ] && jq -c '[select(.state == "OPEN")]' "$GH_STATE/pr" || echo '[]' ;;
  'pr create')
    echo '{"number":7,"state":"OPEN","isDraft":false}' >"$GH_STATE/pr"
    echo 'https://github.com/bitbiter-dev/anichron/pull/7' ;;
  'pr ready')
    draft=false
    [ "${4:-}" = --undo ] && draft=true
    $draft || echo "branch at ready: $(git ls-remote origin refs/heads/feature | cut -f1)" >>"$GH_STATE/pushed-at-ready"
    $draft || [ -f "$GH_STATE/no-ready-event" ] || touch "$GH_STATE/readied"
    jq -c --argjson d "$draft" '.isDraft = $d' "$GH_STATE/pr" >"$GH_STATE/pr.new" && mv "$GH_STATE/pr.new" "$GH_STATE/pr" ;;
  'api --paginate')
    printf '[{"event":"ready_for_review","created_at":"2026-11-12T09:00:00Z"},{"event":"labeled","created_at":"2026-11-13T09:59:00Z"}]\n'
    [ ! -f "$GH_STATE/readied" ] || printf '[{"event":"ready_for_review","created_at":"2026-11-13T10:00:00Z"}]\n' ;;
  'run list')
    if [ -f "$GH_STATE/run-list-fails" ]; then
      left=$(cat "$GH_STATE/run-list-fails")
      if [ -z "$left" ] || [ "$left" -gt 0 ]; then
        [ -z "$left" ] || echo $((left - 1)) >"$GH_STATE/run-list-fails"
        [ ! -f "$GH_STATE/fail-step" ] || advance "$(cat "$GH_STATE/fail-step")"
        if [ -f "$GH_STATE/signal-on-fail" ]; then
          rm "$GH_STATE/signal-on-fail"
          touch "$GH_STATE/all-fail"
          kill -TERM "$(land_pid)"
        fi
        echo 'error connecting to api.github.com' >&2
        exit 1
      fi
    fi
    if [ -f "$GH_STATE/no-runs-on-$6" ]; then echo '[]'; exit 0; fi
    if [ -f "$GH_STATE/runs-$4.later" ] && [ -f "$GH_STATE/listed-$4" ]; then cat "$GH_STATE/runs-$4.later"; else touch "$GH_STATE/listed-$4"; cat "$GH_STATE/runs-$4"; fi ;;
  'run view')
    left=$(cat "$GH_STATE/run-$3.pending" 2>/dev/null || echo 0)
    if [ "$left" -gt 0 ] && [[ "${5:-}" == status* ]]; then
      echo $((left - 1)) >"$GH_STATE/run-$3.pending"
      jq -c '.status = "in_progress" | .conclusion = null' "$GH_STATE/run-$3"
    else
      cat "$GH_STATE/run-$3"
    fi ;;
  'pr checks')
    if [ -f "$GH_STATE/no-checks-yet" ]; then
      rm "$GH_STATE/no-checks-yet"
      echo "no required checks reported on the 'feature' branch" >&2
      exit 1
    fi
    cat "$GH_STATE/checks" ;;
  'pr view')
    if [ -f "$GH_STATE/merge-state.later" ] && [ -f "$GH_STATE/viewed" ]; then state=$(cat "$GH_STATE/merge-state.later"); else touch "$GH_STATE/viewed"; state=$(cat "$GH_STATE/merge-state"); fi
    jq -c --arg m "$state" --arg h "$(git ls-remote origin refs/heads/feature | cut -f1)" --argjson r "$(cat "$GH_STATE/rollup")" \
      '{mergeStateStatus: $m, state, isDraft, headRefOid: $h, statusCheckRollup: $r}' "$GH_STATE/pr" ;;
  'pr merge')
    if [ -f "$GH_STATE/merge-closes" ]; then
      jq -c '.state = "CLOSED"' "$GH_STATE/pr" >"$GH_STATE/pr.new" && mv "$GH_STATE/pr.new" "$GH_STATE/pr"
      exit 1
    fi
    if [ -f "$GH_STATE/merge-hangs" ]; then
      jq -c '.state = "MERGED"' "$GH_STATE/pr" >"$GH_STATE/pr.new" && mv "$GH_STATE/pr.new" "$GH_STATE/pr"
      [ ! -f "$GH_STATE/merge-signals" ] || kill -TERM "$PPID"
      exec sleep 600
    fi
    [ ! -f "$GH_STATE/merge-refused" ] || exit 1
    if [ -f "$GH_STATE/merge-refusals" ]; then
      left=$(cat "$GH_STATE/merge-refusals")
      [ "$left" -eq 0 ] || { echo $((left - 1)) >"$GH_STATE/merge-refusals"; exit 1; }
    fi ;;
  *) echo "unexpected gh call: $*" >&2; exit 9 ;;
esac
EOF
chmod +x "$work/bin/gh" "$work/bin/date" "$work/bin/sleep"

run_json() { printf '{"status":"completed","conclusion":"%s","url":"https://ci/%s","createdAt":"%s","jobs":[{"name":"%s","status":"completed","conclusion":"%s"}]}\n' "$2" "$1" "$5" "$3" "$4"; }

git_as_mira() { git -c user.name=mira -c user.email=mira@example.org "$@"; }

# setup <pr-state-json|none> <ci-conclusion> [conflict]
setup() {
  rm -rf "$work/origin.git" "$work/main" "$work/wt" "$work/state" "$work/other"
  mkdir -p "$work/state"
  git init -q --bare -b master "$work/origin.git"
  git clone -q "$work/origin.git" "$work/main" 2>/dev/null
  printf 'bin/\n' >"$work/main/.gitignore"
  printf 'one\n' >"$work/main/shared.txt"
  git_as_mira -C "$work/main" add -A
  git_as_mira -C "$work/main" commit -q -m base
  git_as_mira -C "$work/main" push -q origin HEAD:master
  git_as_mira -C "$work/main" worktree add -q "$work/wt" -b feature
  printf 'feature\n' >"$work/wt/feature.txt"
  [ "${3:-}" = conflict ] && printf 'mine\n' >"$work/wt/shared.txt"
  git_as_mira -C "$work/wt" add -A
  git_as_mira -C "$work/wt" commit -q -m feature
  mkdir -p "$work/wt/bin" && echo built >"$work/wt/bin/out"
  [ "${3:-}" = conflict ] || git_as_mira -C "$work/wt" push -q origin feature 2>/dev/null
  head0=$(git -C "$work/wt" rev-parse HEAD)

  git clone -q "$work/origin.git" "$work/other" 2>/dev/null
  if [ "${3:-}" = conflict ]; then printf 'theirs\n' >"$work/other/shared.txt"; else printf 'master\n' >"$work/other/master.txt"; fi
  git_as_mira -C "$work/other" add -A
  git_as_mira -C "$work/other" commit -q -m 'master moved'
  git_as_mira -C "$work/other" push -q origin HEAD:master

  [ "$1" = none ] || echo "$1" >"$work/state/pr"
  printf '[{"databaseId":1,"createdAt":"2026-11-13T09:59:00Z"},{"databaseId":2,"createdAt":"2026-11-13T10:00:05Z"},{"databaseId":3,"createdAt":"2026-11-13T10:00:03Z"}]\n' >"$work/state/runs-ci.yml"
  printf '[{"databaseId":4,"createdAt":"2026-11-13T10:00:04Z"}]\n' >"$work/state/runs-codeql.yml"
  run_json 1 success Changes success 2026-11-13T09:59:00Z >"$work/state/run-1"
  run_json 2 failure Changes skipped 2026-11-13T10:00:05Z >"$work/state/run-2"
  run_json 3 "$2" Changes success 2026-11-13T10:00:03Z >"$work/state/run-3"
  run_json 4 success 'Analyze (C#)' success 2026-11-13T10:00:04Z >"$work/state/run-4"
  echo '[{"name":"CI gate","bucket":"pass"},{"name":"CodeQL","bucket":"pass"}]' >"$work/state/checks"
  echo CLEAN >"$work/state/merge-state"
  echo '[]' >"$work/state/rollup"
}

# rollup_check <name> <status> <conclusion> <completedAt>, as statusCheckRollup holds it.
rollup_check() { printf '{"__typename":"CheckRun","name":"%s","status":"%s","conclusion":"%s","completedAt":"%s"}' "$1" "$2" "$3" "$4"; }

# The rollup of a head where CI gate and CodeQL passed, and <extra> checks.
passed() {
  echo "[$(rollup_check 'CI gate' COMPLETED SUCCESS 2026-11-13T10:40:00Z),$(rollup_check CodeQL COMPLETED NEUTRAL 2026-11-13T10:05:00Z)${1:+,$1}]" >"$work/state/rollup"
}

# The branch already on origin with master merged in, so land.sh has nothing to push.
pushed_already() {
  git_as_mira -C "$work/wt" pull -q --no-rebase --no-edit origin master 2>/dev/null
  git_as_mira -C "$work/wt" push -q origin HEAD:feature 2>/dev/null
}

land() {
  (cd "${1:-$work/wt}" && GH_STATE="$work/state" PATH="$work/bin:$PATH" LAND_POLL_SECONDS=0 LAND_TIMEOUT_MINUTES="${2:-0}" LAND_MERGE_SECONDS="${3:-0}" LAND_MERGE_CALL_SECONDS=1 LAND_OUTAGE_MINUTES="${4:-360}" WITH_LOCK_POLL_SECONDS=0 \
    GIT_AUTHOR_NAME=mira GIT_AUTHOR_EMAIL=mira@example.org GIT_COMMITTER_NAME=mira GIT_COMMITTER_EMAIL=mira@example.org \
    "$script" 2>&1)
}

calls() { cat "$work/state/calls" 2>/dev/null; }

echo 'land.sh'

setup '{"number":7,"state":"OPEN","isDraft":false}' success
out=$(land); status=$?
check 'a green PR is merged' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr merge 7 --squash --match-head-commit'* ]]" "exit $status: $out"
merged=$(git -C "$work/origin.git" rev-parse feature)
check 'master is merged in before the push' "git -C '$work/origin.git' merge-base --is-ancestor master feature"
check 'the merge names the pushed commit' "[[ \"\$(calls)\" == *\"--match-head-commit $merged\"* ]]" "$(calls)"
check 'a ready PR stays ready and is pushed' "[[ \"\$(calls)\" != *'pr ready'* ]]" "$(calls)"
check 'the newest run of the pushed commit is waited for, not a draft run or an older one' "[[ \"\$(calls)\" == *'run view 3 --json status'* ]] && [[ \"\$(calls)\" != *'run view 1 --json status'* ]]" "$(calls)"
check 'the worktree is removed, ignored build output included' "[ ! -d '$work/wt' ]"
check 'the local branch is deleted' "! git -C '$work/main' rev-parse --verify --quiet refs/heads/feature >/dev/null"
check 'the remote branch is left to GitHub' "[ -n \"\$(git -C '$work/origin.git' branch --list feature)\" ] && [[ \"\$(calls)\" != *delete* ]]" "$(calls)"
check 'each phase and finished job is announced' "[[ \"\$out\" == *'land: merged origin/master'*'land: pushed feature'*'land: PR #7 is ready'*'land: ci: Changes: success'*'land: codeql: Analyze (C#): success'*'land: every required check passed'*'land: merged PR #7'* ]]" "$out"
check 'the land lock is released' "[ ! -e '$work/main/.git/anichron-locks/land' ]"

setup none success
out=$(land); status=$?
check 'with no PR, the branch is pushed and one opened ready' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr create --fill --base master --head feature'* ]] && [[ \"\$(calls)\" != *'pr ready'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' failure
out=$(land); status=$?
check 'a draft PR is marked ready before the push' "[ \"\$(cat '$work/state/pushed-at-ready')\" = 'branch at ready: $head0' ]" "$(cat "$work/state/pushed-at-ready" 2>/dev/null)"
check 'the push waits for the ready runs on the old head' "[[ \"\$(calls)\" == *'pr ready 7'*'run list --workflow ci.yml --commit $head0'*'run list --workflow codeql.yml --commit $head0'* ]]" "$(calls)"
check 'a red CI run is not merged' "[ $status -eq 1 ] && [[ \"\$(calls)\" != *'pr merge'* ]]" "exit $status: $out"
check 'its PR is a draft again' "[ \"\$(tail -1 '$work/state/calls')\" = 'pr ready 7 --undo' ]" "$(calls)"
check 'the failure names the run' "[[ \"\$out\" == *'CI: failure https://ci/3'* ]]" "$out"
check 'the worktree stays' "[ -d '$work/wt' ]"
check 'the land lock is released after a failure' "[ ! -e '$work/main/.git/anichron-locks/land' ]"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
touch "$work/state/merge-refused"
out=$(land); status=$?
check 'a merge GitHub refuses fails and keeps the worktree' "[ $status -eq 1 ] && [ -d '$work/wt' ]" "exit $status: $out"
check 'a refused merge puts the PR back to draft' "[ \"\$(tail -1 '$work/state/calls')\" = 'pr ready 7 --undo' ]" "$(calls)"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
echo 2 >"$work/state/merge-refusals"
echo BLOCKED >"$work/state/merge-state"
out=$(land "$work/wt" 0 60); status=$?
check 'a merge refused while the PR is BLOCKED is tried again, and merged' "[ $status -eq 0 ] && [ \"\$(grep -c '^pr merge 7' '$work/state/calls')\" -eq 3 ]" "exit $status: $out"
check 'the refusal is announced once' "[ \"\$(grep -c 'GitHub refused the merge of a BLOCKED PR; trying again' <<<\"\$out\")\" -eq 1 ]" "$out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
touch "$work/state/merge-refused"
echo BLOCKED >"$work/state/merge-state"
out=$(land "$work/wt" 0 1); status=$?
check 'a merge still refused when the window ends puts the PR back to draft' "[ $status -eq 1 ] && [ \"\$(grep -c '^pr merge 7' '$work/state/calls')\" -gt 1 ] && [ \"\$(tail -1 '$work/state/calls')\" = 'pr ready 7 --undo' ]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
touch "$work/state/merge-refused"
echo BEHIND >"$work/state/merge-state.later"
out=$(land "$work/wt" 0 60); status=$?
check 'master moving during the merge stops at once and says so' "[ $status -eq 1 ] && [ \"\$(grep -c '^pr merge 7' '$work/state/calls')\" -eq 1 ] && [[ \"\$out\" == *'master moved while CI ran'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
touch "$work/state/merge-refused"
echo DIRTY >"$work/state/merge-state.later"
out=$(land "$work/wt" 0 60); status=$?
check 'a refusal in any other merge state stops at once and names it' "[ $status -eq 1 ] && [ \"\$(grep -c '^pr merge 7' '$work/state/calls')\" -eq 1 ] && [[ \"\$out\" == *'merge state DIRTY'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
touch "$work/state/merge-hangs"
out=$(land); status=$?
check 'a merge call that hangs after GitHub merged is landed and cleaned up' "[ $status -eq 0 ] && [[ \"\$out\" == *'land: merged PR #7'* ]] && [ ! -d '$work/wt' ]" "exit $status: $out"
check 'the hung call is named' "[[ \"\$out\" == *'gh pr merge did not return within 1s'* ]]" "$out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
touch "$work/state/merge-closes"
out=$(land); status=$?
check 'a PR closed when it is merged is not made a draft' "[ $status -eq 1 ] && [[ \"\$out\" == *'PR #7 is closed; not making it a draft'* ]] && [[ \"\$(calls)\" != *'pr ready 7 --undo'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
touch "$work/state/merge-hangs" "$work/state/merge-signals"
out=$(land); status=$?
check 'a stop after the merge makes no draft of the merged PR' "[ $status -eq 3 ] && [[ \"\$out\" == *'PR #7 is merged; not making it a draft'* ]] && [[ \"\$(calls)\" != *'pr ready 7 --undo'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success conflict
before=$(git -C "$work/wt" rev-parse HEAD)
out=$(land); status=$?
check 'a conflict with master stops before pushing' "[ $status -eq 1 ] && [[ \"\$out\" == *'conflicts'* ]] && [ -z \"\$(git -C '$work/origin.git' branch --list feature)\" ]" "exit $status: $out"
check 'the conflicted merge is aborted' "[ \"\$(git -C '$work/wt' rev-parse HEAD)\" = '$before' ] && [ -z \"\$(git -C '$work/wt' status --porcelain)\" ]" "$(git -C "$work/wt" status --short)"

setup '{"number":5,"state":"MERGED","isDraft":false}' success
out=$(land); status=$?
check "a merged PR of a reused branch name is ignored, and a new one opened" "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr create --fill'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' cancelled
printf '[{"databaseId":3,"createdAt":"2026-11-13T10:00:03Z"},{"databaseId":5,"createdAt":"2026-11-13T10:00:06Z"}]\n' >"$work/state/runs-ci.yml.later"
run_json 5 success Changes success 2026-11-13T10:00:06Z >"$work/state/run-5"
out=$(land "$work/wt" 1); status=$?
check 'a run cancelled by a newer ready run gives way to it' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'run view 5 --json status'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' cancelled
out=$(land); status=$?
check 'a cancelled run with no newer one is not merged' "[ $status -eq 1 ] && [[ \"\$(calls)\" != *'pr merge'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
pushed_already
out=$(land); status=$?
check 'with nothing to push, the PR is made a draft and ready again, and that ready run waited for' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr ready 7 --undo'*'pr ready 7'*'run view 3 --json status'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
pushed_already
touch "$work/state/no-ready-event"
out=$(land); status=$?
check "a ready event from an earlier attempt is not taken for this one" "[ $status -eq 1 ] && [[ \"\$out\" == *'ready_for_review event never appeared'* ]] && [[ \"\$(calls)\" != *'run list'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
touch "$work/state/no-runs-on-$head0"
out=$(land); status=$?
check 'ready runs that never start on the old head stop it before the push' "[ $status -eq 1 ] && [[ \"\$out\" == *'never started'* ]] && [ \"\$(git -C '$work/origin.git' rev-parse feature)\" = '$head0' ]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success conflict
git_as_mira -C "$work/wt" push -q origin feature 2>/dev/null
git_as_mira -C "$work/wt" pull -q --no-rebase --no-edit origin master 2>/dev/null
printf 'resolved\n' >"$work/wt/shared.txt"
git_as_mira -C "$work/wt" commit -q -am 'resolve master'
touch "$work/state/no-runs-on-$head0"
out=$(land); status=$?
check 'an old head that conflicts with master, which GitHub starts no run on, is pushed before the PR is marked ready' "[ $status -eq 0 ] && [ \"\$(cat '$work/state/pushed-at-ready')\" = \"branch at ready: \$(git -C '$work/origin.git' rev-parse feature)\" ] && [[ \"\$(calls)\" != *'--commit $head0'* ]] && [[ \"\$(calls)\" == *'pr merge 7'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
echo 1800000000 >"$work/state/clock"
echo 120 >"$work/state/fail-step"
touch "$work/state/run-list-fails"
out=$(land "$work/wt" 1 0 5); status=$?
check 'a gh call failing for longer than the outage cap stops with that reason' "[ $status -eq 1 ] && [[ \"\$out\" == *'gh run list still failing after 5 minutes'* ]] && [[ \"\$out\" == *'CI: gh-failing'* ]] && [ \"\$(grep -c '^run list --workflow ci.yml' '$work/state/calls')\" -eq 3 ]" "exit $status: $out"
check 'its PR is a draft again too' "[ \"\$(tail -1 '$work/state/calls')\" = 'pr ready 7 --undo' ]" "$(calls)"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
echo 1800000000 >"$work/state/clock"
echo 1800 >"$work/state/fail-step"
echo 3 >"$work/state/run-list-fails"
out=$(land "$work/wt" 60); status=$?
check 'an outage longer than the timeout does not count towards it, and the PR is merged' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr merge 7'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
echo 1800000000 >"$work/state/clock"
echo 36000 >"$work/state/fail-step"
echo 3 >"$work/state/run-list-fails"
out=$(land "$work/wt" 60 0 60); status=$?
check 'time asleep between failing calls does not count towards the outage cap' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr merge 7'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
echo 1800000000 >"$work/state/clock"
echo 2 >"$work/state/run-3.pending"
echo 10800 >"$work/state/sleep-jump"
out=$(land "$work/wt" 10); status=$?
check 'a machine asleep during a poll does not time out, and the PR is merged' "[ $status -eq 0 ] && [ \"\$(grep -c '^run view 3 --json status' '$work/state/calls')\" -eq 3 ] && [[ \"\$(calls)\" == *'pr merge 7'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
echo 1800000000 >"$work/state/clock"
touch "$work/state/run-list-fails" "$work/state/signal-on-fail"
out=$(land "$work/wt" 60); status=$?
check 'a signal during an outage stops at once, and within the stop deadline' "[ $status -eq 1 ] && [ \"\$(grep -c '^run list' '$work/state/calls')\" -eq 1 ] && [[ \"\$out\" == *'gh pr view still failing; giving up'*'gh pr ready still failing; giving up'*'stopped by a signal'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
touch "$work/state/no-checks-yet"
out=$(land "$work/wt" 1); status=$?
check 'a PR with no checks reported yet waits for them, and is merged' "[ $status -eq 0 ] && [ \"\$(grep -c '^pr checks 7' '$work/state/calls')\" -eq 2 ] && [[ \"\$out\" != *'pr checks failed'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
pushed_already
passed
out=$(land); status=$?
check 'a ready PR whose head already passed is merged without a new run' "[ $status -eq 0 ] && [[ \"\$(calls)\" != *'pr ready'* ]] && [[ \"\$(calls)\" != *'run list'* ]] && [[ \"\$(calls)\" == *\"pr merge 7 --squash --match-head-commit \$(git -C '$work/origin.git' rev-parse feature)\"* ]]" "exit $status: $out"
check 'the skip is announced' "[[ \"\$out\" == *'land: PR #7 already passed CI at '*'; merging'* ]]" "$out"

for variant in \
  "a CI gate that is only neutral|$(rollup_check 'CI gate' COMPLETED NEUTRAL 2026-11-13T11:00:00Z)" \
  "a newer CI gate still running|$(rollup_check 'CI gate' IN_PROGRESS '' '')" \
  "a newer CI gate that failed|$(rollup_check 'CI gate' COMPLETED FAILURE 2026-11-13T11:00:00Z)" \
  "a newer CodeQL that was skipped|$(rollup_check CodeQL COMPLETED SKIPPED 2026-11-13T11:00:00Z)"; do
  setup '{"number":7,"state":"OPEN","isDraft":false}' success
  pushed_already
  passed "${variant#*|}"
  out=$(land); status=$?
  check "a head with ${variant%%|*} is run again" "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr ready 7 --undo'*'pr ready 7'*'run view 3 --json status'* ]]" "exit $status: $out"
done

setup '{"number":7,"state":"OPEN","isDraft":false}' success
pushed_already
echo "[$(rollup_check CodeQL COMPLETED NEUTRAL 2026-11-13T10:05:00Z)]" >"$work/state/rollup"
out=$(land); status=$?
check 'a head with no CI gate is run again' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr ready 7 --undo'*'pr ready 7'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
pushed_already
passed
echo BEHIND >"$work/state/merge-state"
out=$(land); status=$?
check 'a passed head that is BEHIND is not merged on that pass' "[ $status -eq 1 ] && [[ \"\$(calls)\" == *'pr ready 7 --undo'*'pr ready 7'* ]] && [[ \"\$(calls)\" != *'pr merge'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
passed
out=$(land); status=$?
check 'a passed head with a new commit to push is not merged on that pass' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'run view 3 --json status'* ]] && [[ \"\$out\" != *'already passed'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
passed
touch "$work/state/merge-refused"
out=$(land); status=$?
check 'a stop after every check passed leaves the PR ready, and says so' "[ $status -eq 1 ] && [[ \"\$out\" == *'not merged, PR #7 stays ready: every check passed; run land.sh again.'* ]] && [ \"\$(tail -1 '$work/state/calls')\" != 'pr ready 7 --undo' ]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":false}' success
echo 3 >"$work/state/run-list-fails"
out=$(land "$work/wt" 1); status=$?
check 'an outage that ends before the timeout is ridden out, and the PR merged' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr merge 7'* ]]" "exit $status: $out"
check 'the outage and its end are announced' "[[ \"\$out\" == *'land: gh run list failed; trying again every 0s'*'land: gh run list answers again'* ]]" "$out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
echo '[{"name":"CI gate","bucket":"pass"},{"name":"CodeQL","bucket":"fail"}]' >"$work/state/checks"
out=$(land); status=$?
check 'a failing required check is not merged, and named' "[ $status -eq 1 ] && [[ \"\$out\" == *'Required checks: CodeQL: fail'* ]] && [[ \"\$(calls)\" != *'pr merge'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
echo BEHIND >"$work/state/merge-state"
out=$(land); status=$?
check 'master moving during CI is named, and not merged' "[ $status -eq 1 ] && [[ \"\$out\" == *'master moved while CI ran'* ]] && [[ \"\$(calls)\" != *'pr merge'* ]]" "exit $status: $out"

setup none success
echo dirty >>"$work/wt/feature.txt"
out=$(land); status=$?
check 'uncommitted work is refused' "[ $status -eq 2 ] && [ ! -s '$work/state/calls' ]" "exit $status: $out"

setup none success
out=$(land "$work/main"); status=$?
check 'master is refused' "[ $status -eq 2 ] && [[ \"\$out\" == *'refusing to land master'* ]]" "exit $status: $out"

if [ "$failures" -gt 0 ]; then
  printf '\n%d failure(s)\n' "$failures"
  exit 1
fi
printf '\nall passed\n'
