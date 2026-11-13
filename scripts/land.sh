#!/usr/bin/env bash
# Usage: scripts/land.sh
#
# Lands the current worktree's branch on master, one PR at a time on this
# machine (the `land` lock, scripts/with-lock.sh):
#   1. merges origin/master, aborting on a conflict;
#   2. marks the branch's open PR ready, then pushes, so CI and CodeQL run
#      only on the pushed commit and a draft run never follows the ready one
#      (with no PR, it pushes and opens one ready; with nothing to push, it
#      marks the PR ready again, which starts the runs);
#   3. waits for both runs and every required check, and squash-merges;
#   4. removes the worktree and the local branch. GitHub deletes the remote
#      branch on merge.
# Every message starts with `land: `: one line per phase and per finished CI or
# CodeQL job. A failing `gh` call (GitHub unreachable) is retried until it
# answers, and the state read again then; only a failed run or check, the
# timeout, a signal, or master moving during CI stops it, with the reason.
# LAND_POLL_SECONDS is the wait between checks (default 30), LAND_TIMEOUT_MINUTES
# the longest wait for everything, outages included (default 120).
#
# Exit: 0 merged and cleaned up; 1 not merged, the reason on stderr, and the PR
# a draft again if it had been marked ready; 2 usage; 3 merged, but cleaning up
# failed.
set -euo pipefail

poll="${LAND_POLL_SECONDS:-30}"
timeout_minutes="${LAND_TIMEOUT_MINUTES:-120}"

say() { printf 'land: %s\n' "$*" >&2; }
die() { say "$1"; exit "${2:-1}"; }

# Runs gh until it succeeds or `deadline` passes, saying when it stops and
# starts answering.
gh_retry() {
  local failed=''
  until gh "$@"; do
    [ "$(date +%s)" -lt "$deadline" ] || { say "gh $1 $2 still failing at the timeout"; return 1; }
    [ -n "$failed" ] || say "gh $1 $2 failed; trying again every ${poll}s"
    failed=1
    sleep "$poll"
  done
  [ -z "$failed" ] || say "gh $1 $2 answers again"
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
# cancelled (only a run created after the cancelled one replaces it). Sets `outcome` to its conclusion and url, `gh-failing`, or
# `timed-out`. Runs in the main shell, so a signal stops it within one poll.
await_run() {
  local workflow="$1" sha="$2" since="$3" root_job="$4" deadline="$5" id='' skip='' run newer reported='' job
  while :; do
    if [ -z "$id" ]; then
      id=$(ready_run "$workflow" "$sha" "$since" "$root_job" "$skip") || { outcome=gh-failing; return; }
    fi
    if [ -n "$id" ]; then
      run=$(gh_retry run view "$id" --json status,conclusion,url,jobs,createdAt) || { outcome="gh-failing $id"; return; }
      while IFS= read -r job; do
        [ -n "$job" ] || continue
        grep -qxF -- "$job" <<<"$reported" || { say "${workflow%.yml}: $job"; reported="$reported"$'\n'"$job"; }
      done < <(jq -r '.jobs[] | select(.status == "completed" and .conclusion != "skipped") | "\(.name): \(.conclusion)"' <<<"$run")
      if [ "$(jq -r .status <<<"$run")" = completed ]; then
        if [ "$(jq -r .conclusion <<<"$run")" = cancelled ]; then
          newer=$(ready_run "$workflow" "$sha" "$(jq -r .createdAt <<<"$run")" "$root_job" "$skip $id") || { outcome="gh-failing $id"; return; }
          if [ -n "$newer" ]; then
            skip="$skip $id"
            id="$newer"
            continue
          fi
        fi
        outcome=$(jq -r '"\(.conclusion) \(.url)"' <<<"$run")
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
  say "merged origin/master into $branch"

  deadline=$(($(date +%s) + timeout_minutes * 60))
  local pr number url sha remote events seen
  sha=$(git rev-parse HEAD)
  remote=$(git ls-remote origin "refs/heads/$branch" | cut -f1) || die "cannot read origin's $branch"
  pr=$(gh_retry pr list --head "$branch" --state open --json number,isDraft) || die 'cannot list the open PRs'
  number=$(jq -r '.[0].number // empty' <<<"$pr")

  back_to_draft() {
    trap - TERM HUP INT
    deadline=$(($(date +%s) + 600))
    gh_retry pr ready "$number" --undo >/dev/null || say "PR #$number could not be made a draft again"
    die "not merged, PR #$number is a draft again. $1"
  }

  since=''
  if [ -z "$number" ]; then
    git push --quiet --set-upstream origin "$branch" || die "cannot push $branch"
    say "pushed $branch"
    url=$(gh pr create --fill --base master --head "$branch") || die 'cannot open a PR'
    number="${url##*/}"
    [[ "$number" =~ ^[0-9]+$ ]] || die "cannot read the PR number from '$url'"
    trap 'back_to_draft "land.sh was stopped by a signal."' TERM HUP INT
  elif [ "$remote" != "$sha" ]; then
    trap 'back_to_draft "land.sh was stopped by a signal."' TERM HUP INT
    if [ "$(jq -r '.[0].isDraft' <<<"$pr")" = true ]; then
      gh_retry pr ready "$number" >/dev/null || back_to_draft "Marking it ready failed."
    fi
    git push --quiet --set-upstream origin "$branch" || back_to_draft "Pushing $branch failed."
    say "pushed $branch"
  else
    events=$(ready_events "$number") || die "cannot read the events of PR #$number"
    seen=$(grep -c . <<<"$events" || true)
    trap 'back_to_draft "land.sh was stopped by a signal."' TERM HUP INT
    if [ "$(jq -r '.[0].isDraft' <<<"$pr")" != true ]; then
      gh_retry pr ready "$number" --undo >/dev/null || back_to_draft "Making it a draft failed."
    fi
    gh_retry pr ready "$number" >/dev/null || back_to_draft "Marking it ready failed."
    ready_since "$number" "$seen" "$deadline" || back_to_draft 'Its ready event could not be read.'
  fi
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
  say 'every required check passed'

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
