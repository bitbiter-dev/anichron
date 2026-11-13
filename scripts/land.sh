#!/usr/bin/env bash
# Usage: scripts/land.sh
#
# Lands the current worktree's branch on master, one PR at a time on this
# machine (the `land` lock, scripts/with-lock.sh):
#   1. merges origin/master, aborting on a conflict;
#   2. pushes while the branch's open PR is a draft (opening one as a draft
#      when there is none), then marks it ready, which starts CI and CodeQL;
#   3. waits for both runs and every required check, and squash-merges;
#   4. removes the worktree and the local branch. GitHub deletes the remote
#      branch on merge.
# A `gh` call that keeps failing, a timeout, a signal, or master moving during
# CI stops it with the reason. LAND_POLL_SECONDS is the wait between checks
# (default 30), LAND_TIMEOUT_MINUTES the longest wait for the runs and checks
# together (default 120).
#
# Exit: 0 merged and cleaned up; 1 not merged, the reason on stderr, and the PR
# a draft again if it had been marked ready; 2 usage; 3 merged, but cleaning up
# failed.
set -euo pipefail

poll="${LAND_POLL_SECONDS:-30}"
timeout_minutes="${LAND_TIMEOUT_MINUTES:-120}"
attempts=5

say() { printf '%s\n' "$*" >&2; }
die() { say "$1"; exit "${2:-1}"; }

# Runs gh, retrying a failed call up to $attempts times.
gh_retry() {
  local i
  for ((i = 1; i <= attempts; i++)); do
    gh "$@" && return
    [ "$i" -eq "$attempts" ] || sleep "$poll"
  done
  say "gh $1 $2 failed $attempts times in a row"
  return 1
}

# The newest run of <workflow> on <sha> created since <since>, other than the
# ids in <skip>, whose <root-job> is not skipped as a draft's is.
ready_run() {
  local workflow="$1" sha="$2" since="$3" root_job="$4" skip="$5" runs id jobs
  runs=$(gh_retry run list --workflow "$workflow" --commit "$sha" --event pull_request --json databaseId,createdAt) || return 1
  for id in $(jq -r --arg since "$since" '[.[] | select(.createdAt >= $since)] | sort_by(.createdAt) | reverse | .[].databaseId' <<<"$runs"); do
    [[ " $skip " != *" $id "* ]] || continue
    jobs=$(gh_retry run view "$id" --json jobs) || return 1
    if jq -e --arg job "$root_job" 'any(.jobs[]; .name == $job and .conclusion != "skipped")' <<<"$jobs" >/dev/null; then
      echo "$id"
      return
    fi
  done
}

# Waits for the ready run of <workflow> until <deadline>, past runs a newer one
# cancelled. Sets `outcome` to its conclusion and url, `gh-failing`, or
# `timed-out`. Runs in the main shell, so a signal stops it within one poll.
await_run() {
  local workflow="$1" sha="$2" since="$3" root_job="$4" deadline="$5" id='' skip='' run newer
  while :; do
    if [ -z "$id" ]; then
      id=$(ready_run "$workflow" "$sha" "$since" "$root_job" "$skip") || { outcome=gh-failing; return; }
    fi
    if [ -n "$id" ]; then
      run=$(gh_retry run view "$id" --json status,conclusion,url,jobs) || { outcome="gh-failing $id"; return; }
      if [ "$(jq -r .status <<<"$run")" = completed ]; then
        if [ "$(jq -r .conclusion <<<"$run")" = cancelled ]; then
          newer=$(ready_run "$workflow" "$sha" "$since" "$root_job" "$skip $id") || { outcome="gh-failing $id"; return; }
          if [ -n "$newer" ]; then
            skip="$skip $id"
            id="$newer"
            continue
          fi
        fi
        outcome=$(jq -r '"\(.conclusion) \(.url)"' <<<"$run")
        jq -r '.jobs[] | select(.conclusion == "failure" or .conclusion == "cancelled" or .conclusion == "timed_out") | "  \(.name): \(.conclusion)"' <<<"$run" >&2
        return
      fi
    fi
    [ "$(date +%s)" -lt "$deadline" ] || { outcome="timed-out ${id:-no-run}"; return; }
    sleep "$poll"
  done
}

# The PR's ready_for_review events, oldest first.
ready_events() {
  gh_retry api --paginate "repos/{owner}/{repo}/issues/$1/events" \
    | jq -r '.[] | select(.event == "ready_for_review") | .created_at'
}

# Waits for a ready_for_review event beyond the first <seen>; sets `since` to
# its time.
ready_since() {
  local number="$1" seen="$2" deadline="$3" events
  while :; do
    events=$(ready_events "$number") || return 1
    if [ "$(grep -c . <<<"$events" || true)" -gt "$seen" ]; then
      since=$(tail -n 1 <<<"$events")
      return
    fi
    [ "$(date +%s)" -lt "$deadline" ] || { say 'its ready_for_review event never appeared'; return 1; }
    sleep "$poll"
  done
}

# Waits until no required check is pending; sets `failing` to each failing one.
await_required() {
  local number="$1" deadline="$2" checks
  while :; do
    checks=$(gh_retry pr checks "$number" --required --json name,bucket) || { failing='gh is failing'; return; }
    if ! jq -e 'any(.[]; .bucket == "pending")' <<<"$checks" >/dev/null; then
      failing=$(jq -r '.[] | select(.bucket == "fail" or .bucket == "cancel") | "\(.name): \(.bucket)"' <<<"$checks")
      return
    fi
    [ "$(date +%s)" -lt "$deadline" ] || { failing='required checks still pending at the timeout'; return; }
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
    git merge --abort 2>/dev/null || true
    die 'merging origin/master failed (see above): resolve it yourself, commit, and run land.sh again'
  fi

  local pr number url
  pr=$(gh_retry pr list --head "$branch" --state open --json number,isDraft) || die 'cannot list the open PRs'
  number=$(jq -r '.[0].number // empty' <<<"$pr")
  if [ -n "$number" ] && [ "$(jq -r '.[0].isDraft' <<<"$pr")" != true ]; then
    gh_retry pr ready "$number" --undo >/dev/null || die "cannot make PR #$number a draft"
  fi
  git push --quiet --set-upstream origin "$branch" || die "cannot push $branch"
  if [ -z "$number" ]; then
    url=$(gh pr create --draft --fill --base master --head "$branch") || die 'cannot open a draft PR'
    number="${url##*/}"
    [[ "$number" =~ ^[0-9]+$ ]] || die "cannot read the PR number from '$url'"
  fi

  local sha deadline events seen
  sha=$(git rev-parse HEAD)
  deadline=$(($(date +%s) + timeout_minutes * 60))
  events=$(ready_events "$number") || die "cannot read the events of PR #$number"
  seen=$(grep -c . <<<"$events" || true)

  back_to_draft() {
    trap - TERM HUP INT
    gh_retry pr ready "$number" --undo >/dev/null || say "PR #$number could not be made a draft again"
    die "not merged, PR #$number is a draft again. $1"
  }
  trap 'back_to_draft "land.sh was stopped by a signal."' TERM HUP INT
  gh_retry pr ready "$number" >/dev/null || back_to_draft "Marking it ready failed."
  since=''
  ready_since "$number" "$seen" "$deadline" || back_to_draft 'Its ready event could not be read.'
  say "PR #$number is ready at $sha; waiting for CI and CodeQL"

  local ci codeql state
  outcome=''
  await_run ci.yml "$sha" "$since" Changes "$deadline"
  ci="$outcome"
  await_run codeql.yml "$sha" "$since" 'Analyze (C#)' "$deadline"
  codeql="$outcome"
  if [ "${ci%% *}" != success ] || [ "${codeql%% *}" != success ]; then
    back_to_draft "CI: $ci. CodeQL: $codeql"
  fi
  failing=''
  await_required "$number" "$deadline"
  [ -z "$failing" ] || back_to_draft "Required checks: $failing"

  state=$(gh_retry pr view "$number" --json mergeStateStatus | jq -r .mergeStateStatus) || back_to_draft 'Its merge state could not be read.'
  [ "$state" != BEHIND ] || back_to_draft 'master moved while CI ran; run land.sh again to merge it in.'
  gh pr merge "$number" --squash --match-head-commit "$sha" >/dev/null \
    || back_to_draft "GitHub refused the merge (merge state $state, gh's reason above)."
  trap - TERM HUP INT
  say "merged PR #$number"

  local top main_checkout
  top=$(git rev-parse --show-toplevel) || die "merged PR #$number, but cannot find the worktree to remove" 3
  main_checkout=$(git worktree list --porcelain | awk 'NR == 1 { sub(/^worktree /, ""); print }')
  cd "$main_checkout" || die "merged PR #$number, but cannot reach the main checkout to clean up" 3
  if [ "$top" != "$main_checkout" ]; then
    git worktree remove "$top" || die "merged PR #$number, but removing $top failed" 3
    git branch --quiet -D "$branch" || die "merged PR #$number, but deleting branch $branch failed" 3
  fi
  git fetch --quiet --prune origin || die "merged PR #$number and removed $top, but git fetch --prune failed" 3
  say "removed $top and branch $branch"
  exit 0
}

main "$@"
