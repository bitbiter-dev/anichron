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
cat >"$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
echo "$*" >>"$GH_STATE/calls"
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
        echo 'error connecting to api.github.com' >&2
        exit 1
      fi
    fi
    if [ -f "$GH_STATE/runs-$4.later" ] && [ -f "$GH_STATE/listed-$4" ]; then cat "$GH_STATE/runs-$4.later"; else touch "$GH_STATE/listed-$4"; cat "$GH_STATE/runs-$4"; fi ;;
  'run view') cat "$GH_STATE/run-$3" ;;
  'pr checks') cat "$GH_STATE/checks" ;;
  'pr view') printf '{"mergeStateStatus":"%s"}\n' "$(cat "$GH_STATE/merge-state")" ;;
  'pr merge') [ ! -f "$GH_STATE/merge-refused" ] ;;
  *) echo "unexpected gh call: $*" >&2; exit 9 ;;
esac
EOF
chmod +x "$work/bin/gh"

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
}

# The branch already on origin with master merged in, so land.sh has nothing to push.
pushed_already() {
  git_as_mira -C "$work/wt" pull -q --no-rebase --no-edit origin master 2>/dev/null
  git_as_mira -C "$work/wt" push -q origin HEAD:feature 2>/dev/null
}

land() {
  (cd "${1:-$work/wt}" && GH_STATE="$work/state" PATH="$work/bin:$PATH" LAND_POLL_SECONDS=0 LAND_TIMEOUT_MINUTES="${2:-0}" WITH_LOCK_POLL_SECONDS=0 \
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
check 'a draft PR is marked ready before the push' "[ \"\$(cat '$work/state/pushed-at-ready')\" = 'branch at ready: ' ]" "$(cat "$work/state/pushed-at-ready" 2>/dev/null)"
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

setup '{"number":7,"state":"OPEN","isDraft":true}' success conflict
before=$(git -C "$work/wt" rev-parse HEAD)
out=$(land); status=$?
check 'a conflict with master stops before pushing' "[ $status -eq 1 ] && [[ \"\$out\" == *'conflicts'* ]] && [ -z \"\$(git -C '$work/origin.git' branch --list feature)\" ]" "exit $status: $out"
check 'the conflicted merge is aborted' "[ \"\$(git -C '$work/wt' rev-parse HEAD)\" = '$before' ] && [ -z \"\$(git -C '$work/wt' status --porcelain)\" ]" "$(git -C "$work/wt" status --short)"

setup '{"number":5,"state":"MERGED","isDraft":false}' success
out=$(land); status=$?
check "a merged PR of a reused branch name is ignored, and a new one opened" "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'pr create --fill'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' cancelled
printf '[{"databaseId":3,"createdAt":"2026-11-13T10:00:03Z"},{"databaseId":5,"createdAt":"2026-11-13T10:00:06Z"}]\n' >"$work/state/runs-ci.yml.later"
run_json 5 success Changes success 2026-11-13T10:00:06Z >"$work/state/run-5"
out=$(land "$work/wt" 1); status=$?
check 'a run cancelled by a newer ready run gives way to it' "[ $status -eq 0 ] && [[ \"\$(calls)\" == *'run view 5 --json status'* ]]" "exit $status: $out"

setup '{"number":7,"state":"OPEN","isDraft":true}' cancelled
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
touch "$work/state/run-list-fails"
out=$(land); status=$?
check 'a gh call still failing at the timeout stops with that reason' "[ $status -eq 1 ] && [[ \"\$out\" == *'gh run list still failing at the timeout'* ]] && [[ \"\$out\" == *'CI: gh-failing'* ]]" "exit $status: $out"
check 'its PR is a draft again too' "[ \"\$(tail -1 '$work/state/calls')\" = 'pr ready 7 --undo' ]" "$(calls)"

setup '{"number":7,"state":"OPEN","isDraft":true}' success
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
