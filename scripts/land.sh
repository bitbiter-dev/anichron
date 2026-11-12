#!/usr/bin/env bash
# Usage: scripts/land.sh
#
# Lands the current worktree's branch on master, one PR at a time on this
# machine (the `land` lock, scripts/with-lock.sh):
#   1. merges origin/master, aborting on a conflict;
#   2. pushes while the branch's open PR is a draft (opening one as a draft
#      when there is none), then marks it ready, which starts CI and CodeQL;
#   3. waits for both runs and squash-merges;
#   4. removes the worktree and the local branch. GitHub deletes the remote
#      branch on merge.
# LAND_POLL_SECONDS is the wait between checks (default 30), LAND_TIMEOUT_MINUTES
# the longest wait for both runs together (default 120).
#
# Exit: 0 merged and cleaned up; 1 not merged, the reason on stderr, and the PR
# a draft again if it had been marked ready; 2 usage; 3 merged, but the worktree
# or branch could not be removed.
set -euo pipefail

poll="${LAND_POLL_SECONDS:-30}"
timeout_minutes="${LAND_TIMEOUT_MINUTES:-120}"

say() { printf '%s\n' "$*" >&2; }
die() { say "$1"; exit "${2:-1}"; }

# The newest run of <workflow> on <sha> created since <since>, other than the
# ids in <skip>, whose <root-job> is not skipped as a draft's is.
ready_run() {
  local workflow="$1" sha="$2" since="$3" root_job="$4" skip="$5" id
  for id in $(gh run list --workflow "$workflow" --commit "$sha" --event pull_request \
    --json databaseId,createdAt \
    | jq -r --arg since "$since" '[.[] | select(.createdAt >= $since)] | sort_by(.createdAt) | reverse | .[].databaseId'); do
    [[ " $skip " != *" $id "* ]] || continue
    if gh run view "$id" --json jobs \
      | jq -e --arg job "$root_job" 'any(.jobs[]; .name == $job and .conclusion != "skipped")' >/dev/null; then
      echo "$id"
      return
    fi
  done
}

# Waits for the ready run of <workflow> until <deadline>, past runs a newer one
# cancelled; prints its conclusion and url.
await_run() {
  local workflow="$1" sha="$2" since="$3" root_job="$4" deadline="$5" id='' skip='' run conclusion
  while :; do
    [ -n "$id" ] || id=$(ready_run "$workflow" "$sha" "$since" "$root_job" "$skip")
    if [ -n "$id" ]; then
      run=$(gh run view "$id" --json status,conclusion,url,jobs)
      if [ "$(jq -r .status <<<"$run")" = completed ]; then
        conclusion=$(jq -r .conclusion <<<"$run")
        if [ "$conclusion" = cancelled ] && [ -n "$(ready_run "$workflow" "$sha" "$since" "$root_job" "$skip $id")" ]; then
          skip="$skip $id"
          id=''
          continue
        fi
        jq -r '"\(.conclusion) \(.url)"' <<<"$run"
        jq -r '.jobs[] | select(.conclusion == "failure" or .conclusion == "cancelled" or .conclusion == "timed_out") | "  \(.name): \(.conclusion)"' <<<"$run" >&2
        return
      fi
    fi
    [ "$(date +%s)" -lt "$deadline" ] || { echo "timed-out ${id:-no-run}"; return; }
    sleep "$poll"
  done
}

# When the PR was last marked ready, from its events.
ready_since() {
  local number="$1" deadline="$2" since
  while :; do
    since=$(gh api --paginate "repos/{owner}/{repo}/issues/$number/events" \
      | jq -r '.[] | select(.event == "ready_for_review") | .created_at' | tail -n 1)
    [ -z "$since" ] || { echo "$since"; return; }
    [ "$(date +%s)" -lt "$deadline" ] || return 1
    sleep "$poll"
  done
}

main() {
  [ "$#" -eq 0 ] || die 'usage: scripts/land.sh (from the worktree of the branch to land)' 2

  local here branch
  here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  branch=$(git symbolic-ref --quiet --short HEAD) || die 'not on a branch' 2
  [ "$branch" != master ] || die 'refusing to land master' 2
  [ -z "$(git status --porcelain)" ] || die 'the worktree has uncommitted or untracked files; commit them first' 2

  if [ -z "${LAND_LOCKED:-}" ]; then
    LAND_LOCKED=1 exec "$here/with-lock.sh" land "$here/land.sh"
  fi

  git fetch --quiet origin master || die 'cannot fetch origin/master'
  if ! git merge --quiet --no-edit origin/master; then
    git merge --abort
    die 'merging origin/master conflicts: merge it yourself, resolve, commit, and run land.sh again'
  fi

  local pr number
  pr=$(gh pr list --head "$branch" --state open --json number,isDraft) || die 'cannot list the open PRs'
  number=$(jq -r '.[0].number // empty' <<<"$pr")
  if [ -n "$number" ] && [ "$(jq -r '.[0].isDraft' <<<"$pr")" != true ]; then
    gh pr ready "$number" --undo >/dev/null || die "cannot make PR #$number a draft"
  fi
  git push --quiet --set-upstream origin "$branch" || die "cannot push $branch"
  if [ -z "$number" ]; then
    gh pr create --draft --fill --base master --head "$branch" >/dev/null || die 'cannot open a draft PR'
    number=$(gh pr list --head "$branch" --state open --json number | jq -r '.[0].number')
  fi

  local sha deadline since
  sha=$(git rev-parse HEAD)
  deadline=$(($(date +%s) + timeout_minutes * 60))
  gh pr ready "$number" >/dev/null || die "cannot mark PR #$number ready"
  back_to_draft() {
    gh pr ready "$number" --undo >/dev/null || say "PR #$number could not be made a draft again"
    die "not merged, PR #$number is a draft again. $1"
  }
  since=$(ready_since "$number" "$deadline") || back_to_draft 'Its ready_for_review event never appeared.'
  say "PR #$number is ready at $sha; waiting for CI and CodeQL"

  local ci codeql
  ci=$(await_run ci.yml "$sha" "$since" Changes "$deadline")
  codeql=$(await_run codeql.yml "$sha" "$since" 'Analyze (C#)' "$deadline")
  if [ "${ci%% *}" != success ] || [ "${codeql%% *}" != success ]; then
    back_to_draft "CI: $ci. CodeQL: $codeql"
  fi

  gh pr merge "$number" --squash --match-head-commit "$sha" >/dev/null \
    || back_to_draft "GitHub refused the merge; see gh pr checks $number"
  say "merged PR #$number"

  local top main_checkout cleaned=true
  top=$(git rev-parse --show-toplevel)
  main_checkout=$(git worktree list --porcelain | awk 'NR == 1 { sub(/^worktree /, ""); print }')
  cd "$main_checkout"
  if [ "$top" != "$main_checkout" ]; then
    git worktree remove "$top" || cleaned=false
    git branch --quiet -D "$branch" || cleaned=false
  fi
  git fetch --quiet --prune origin || cleaned=false
  $cleaned || die "merged PR #$number, but cleaning up $top failed" 3
  say "removed $top and branch $branch"
  exit 0
}

main "$@"
