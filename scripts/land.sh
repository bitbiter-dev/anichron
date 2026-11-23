#!/usr/bin/env bash
# Usage: scripts/land.sh
#
# Lands the current worktree's branch on master, one PR at a time on this
# machine (the `land` lock, scripts/with-lock.sh):
#   1. merges origin/master, aborting on a conflict;
#   2. marks the branch's open PR ready, waits for the runs that starts on the
#      old head, then pushes (with no PR, it pushes and opens one ready; with
#      nothing to push, or an old head that conflicts with master and so gets
#      no run, it pushes first and then marks the PR ready);
#   3. waits for both runs and every required check, and squash-merges. A
#      ready PR with nothing to merge in or push, whose head already passed
#      `CI gate` and `CodeQL` and is not BEHIND, is merged without a new run;
#   4. removes the worktree and the local branch. GitHub deletes the remote
#      branch on merge.
# Every message starts with `land: `: one line per phase and per finished CI or
# CodeQL job. A failing `gh` call (GitHub unreachable) is retried until it
# answers, and the state read again then; only a failed run or check, the
# timeout, the outage cap, a signal, or master moving during CI stops it, with
# the reason. LAND_POLL_SECONDS is the wait between checks (default 30),
# LAND_TIMEOUT_MINUTES the longest wait for CI and the required checks, not
# counting time asleep or with `gh` failing (default 120), LAND_OUTAGE_MINUTES
# the longest one `gh` call is retried while it keeps failing, not counting
# time asleep (default 360), LAND_MERGE_SECONDS how long a merge GitHub refuses while it still
# reports the PR BLOCKED or UNKNOWN is tried again (default 300), and
# LAND_MERGE_CALL_SECONDS how long one merge call may take (default 120). After
# a merge call fails or times out, the PR's state decides: a merged PR is
# cleaned up, a closed one stops it.
#
# Exit: 0 merged and cleaned up; 1 not merged, the reason on stderr, and the PR
# a draft again if it had been marked ready and is still open, or still ready
# if its head passed every required check (run land.sh again to merge it); 2
# usage; 3 merged, but cleaning up failed or was stopped.
set -euo pipefail

poll="${LAND_POLL_SECONDS:-30}"
timeout_minutes="${LAND_TIMEOUT_MINUTES:-120}"
merge_seconds="${LAND_MERGE_SECONDS:-300}"
merge_call_seconds="${LAND_MERGE_CALL_SECONDS:-120}"
outage_minutes="${LAND_OUTAGE_MINUTES:-360}"
slack_seconds=120
passed_fields=isDraft,headRefOid,mergeStateStatus,statusCheckRollup
stop_by=''

say() { printf 'land: %s\n' "$*" >&2; }
die() { say "$1"; exit "${2:-1}"; }

# Runs gh, reading `gh pr checks`' "no checks reported" as an empty list.
gh_call() {
  local err status=0
  { err=$(gh "$@" 2>&1 >&3 3>&-) || status=$?; } 3>&1
  if [ "$status" -ne 0 ] && [ "$1 $2" = 'pr checks' ] && [[ "$err" == *'no checks reported'* ]]; then
    echo '[]'
    return
  fi
  [ -z "$err" ] || printf '%s\n' "$err" >&2
  return "$status"
}

# Runs gh until it succeeds, saying when it stops and starts answering. It gives
# up once the gaps between failing calls, each counted up to one poll and the
# slack, add up to the outage cap, or once `stop_by` passes.
gh_retry() {
  local failed='' outage=0 then now
  then=$(date +%s)
  until gh_call "$@"; do
    now=$(date +%s)
    outage=$((outage + (now - then < poll + slack_seconds ? now - then : poll + slack_seconds)))
    then=$now
    [ -z "$stop_by" ] || [ "$now" -lt "$stop_by" ] || { say "gh $1 $2 still failing; giving up"; return 1; }
    [ "$outage" -lt $((outage_minutes * 60)) ] || { say "gh $1 $2 still failing after ${outage_minutes} minutes"; return 1; }
    [ -n "$failed" ] || say "gh $1 $2 failed; trying again every ${poll}s"
    failed=1
    sleep "$poll"
  done
  [ -z "$failed" ] || say "gh $1 $2 answers again"
}

# Moves `deadline` out by whatever the time since the last pass exceeds one poll
# and the slack: time asleep or with gh failing. Sets `last` to now.
tick() {
  local now
  now=$(date +%s)
  [ $((now - last)) -le $((poll + slack_seconds)) ] || deadline=$((deadline + now - last - poll - slack_seconds))
  last=$now
}

# Whether the PR view <view> shows every required check passed at <sha>: the
# PR ready and not BEHIND, and `CI gate` and `CodeQL` each completed, the
# newest of each a success (neutral is how CodeQL passes).
passes() {
  jq -e --arg sha "$2" '
    def newest($name): [.statusCheckRollup[]? | select(.name == $name)]
      | if length > 0 and all(.[]; .status == "COMPLETED") then max_by(.completedAt) else null end;
    . as $pr
    | .headRefOid == $sha and .isDraft == false and .mergeStateStatus != "BEHIND"
      and all("CI gate", "CodeQL"; . as $name | $pr | newest($name) | . != null and (.conclusion == "SUCCESS" or .conclusion == "NEUTRAL"))
  ' <<<"$1" >/dev/null 2>&1
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

# Waits for the ready run of <workflow> until `deadline`, past runs a newer one
# cancelled: a run created after the cancelled one. Sets `outcome` to its conclusion and url, `gh-failing`, or
# `timed-out`. Runs in the main shell, so a signal stops it within one poll.
await_run() {
  local workflow="$1" sha="$2" since="$3" root_job="$4" id='' skip='' run newer reported='' job
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
    tick
    [ "$last" -lt "$deadline" ] || { outcome="timed-out ${id:-no-run}"; return; }
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
  local number="$1" seen="$2" events
  while :; do
    events=$(ready_events "$number") || return 1
    if [ "$(grep -c . <<<"$events" || true)" -gt "$seen" ]; then
      since=$(tail -n 1 <<<"$events")
      return
    fi
    tick
    [ "$last" -lt "$deadline" ] || { say 'its ready_for_review event never appeared'; return 1; }
    sleep "$poll"
  done
}

# Whether <sha> merges into origin/master cleanly: GitHub starts no
# pull_request run on a head that conflicts with its base.
merges_cleanly() {
  git merge-tree --write-tree --quiet origin/master "$1" >/dev/null 2>&1
}

# Waits until <workflow> has a run on <sha> created since <since>.
await_started() {
  local workflow="$1" sha="$2" since="$3" runs
  while :; do
    runs=$(gh_retry run list --workflow "$workflow" --commit "$sha" --event pull_request --json createdAt) || return 1
    jq -e --arg since "$since" 'any(.[]; .createdAt >= $since)' <<<"$runs" >/dev/null && return
    tick
    [ "$last" -lt "$deadline" ] || { say "$workflow never started on $sha"; return 1; }
    sleep "$poll"
  done
}

# Runs one merge call of <number> at <sha> for at most merge_call_seconds.
merge_once() {
  local watchdog status
  gh pr merge "$1" --squash --match-head-commit "$2" >/dev/null &
  merging=$!
  (
    trap 'kill "$sleeper" 2>/dev/null' TERM
    sleep "$merge_call_seconds" &
    sleeper=$!
    wait "$sleeper" && kill "$merging"
  ) >/dev/null 2>&1 &
  watchdog=$!
  wait "$merging" && status=0 || status=$?
  kill "$watchdog" 2>/dev/null || true
  merging=''
  [ "$status" -ne 143 ] || say "gh pr merge did not return within ${merge_call_seconds}s"
  return "$status"
}

# Squash-merges <number> at <sha>. A refusal while GitHub reports the PR
# BLOCKED or UNKNOWN is tried again for up to merge_seconds; any other merge
# state stops it, and a PR GitHub reports merged counts as merged. Sets `state`
# to the last merge state read.
merge_pr() {
  local number="$1" sha="$2" refuse_by=$(($(date +%s) + merge_seconds)) refused='' view
  until merge_once "$number" "$sha"; do
    view=$(gh_retry pr view "$number" --json state,mergeStateStatus) || return 1
    [ "$(jq -r .state <<<"$view")" != MERGED ] || return 0
    state=$(jq -r .mergeStateStatus <<<"$view")
    case "$state" in BLOCKED | UNKNOWN) ;; *) return 1 ;; esac
    [ "$(date +%s)" -lt "$refuse_by" ] || return 1
    [ -n "$refused" ] || say "GitHub refused the merge of a $state PR; trying again every ${poll}s"
    refused=1
    sleep "$poll"
  done
}

# Waits until the required checks are reported and none is pending; sets
# `failing` to each failing one.
await_required() {
  local number="$1" checks
  while :; do
    checks=$(gh_retry pr checks "$number" --required --json name,bucket) || { failing='gh is failing'; return; }
    if jq -e 'length > 0 and all(.[]; .bucket != "pending")' <<<"$checks" >/dev/null; then
      failing=$(jq -r '.[] | select(.bucket == "fail" or .bucket == "cancel") | "\(.name): \(.bucket)"' <<<"$checks")
      return
    fi
    tick
    [ "$last" -lt "$deadline" ] || { failing='required checks still pending at the timeout'; return; }
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

  local before
  before=$(git rev-parse HEAD)
  git fetch --quiet origin master || die 'cannot fetch origin/master'
  if ! git merge --quiet --no-edit origin/master; then
    git merge --abort 2>/dev/null || true
    die 'merging origin/master failed (see above): resolve it yourself, commit, and run land.sh again'
  fi
  say "merged origin/master into $branch"

  last=$(date +%s)
  deadline=$((last + timeout_minutes * 60))
  local pr number draft url='' sha remote events seen
  sha=$(git rev-parse HEAD)
  remote=$(git ls-remote origin "refs/heads/$branch" | cut -f1) || die "cannot read origin's $branch"
  pr=$(gh_retry pr list --head "$branch" --state open --json number,isDraft) || die 'cannot list the open PRs'
  number=$(jq -r '.[0].number // empty' <<<"$pr")
  draft=$(jq -r '.[0].isDraft // false' <<<"$pr")

  merging=''
  back_to_draft() {
    trap - TERM HUP INT
    [ -z "$merging" ] || kill "$merging" 2>/dev/null || true
    stop_by=$(($(date +%s) + 600))
    local view
    view=$(gh_retry pr view "$number" --json "state,$passed_fields") || view='{}'
    case "$(jq -r .state <<<"$view")" in
      MERGED) die "PR #$number is merged; not making it a draft, and not cleaned up. $1" 3 ;;
      CLOSED) die "PR #$number is closed; not making it a draft. $1" ;;
    esac
    ! passes "$view" "$sha" || die "not merged, PR #$number stays ready: every check passed; run land.sh again. $1"
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
  fi
  trap 'back_to_draft "land.sh was stopped by a signal."' TERM HUP INT
  make_ready() {
    events=$(ready_events "$number") || back_to_draft "Its events could not be read."
    seen=$(grep -c . <<<"$events" || true)
    if [ "$draft" != true ]; then
      gh_retry pr ready "$number" --undo >/dev/null || back_to_draft "Making it a draft failed."
    fi
    gh_retry pr ready "$number" >/dev/null || back_to_draft "Marking it ready failed."
    ready_since "$number" "$seen" || back_to_draft 'Its ready event could not be read.'
  }

  local ci codeql state view
  view='{}'
  if [ -z "$url" ] && [ "$draft" = false ] && [ "$before" = "$sha" ] && [ "$remote" = "$sha" ]; then
    view=$(gh_retry pr view "$number" --json "$passed_fields") || view='{}'
  fi
  if passes "$view" "$sha"; then
    say "PR #$number already passed CI at $sha; merging"
  else
    if [ -z "$url" ] && [ "$remote" != "$sha" ]; then
      if [ "$draft" = true ] && merges_cleanly "$remote"; then
        make_ready
        await_started ci.yml "$remote" "$since" && await_started codeql.yml "$remote" "$since" \
          || back_to_draft 'Its ready runs on the old head never started.'
        since=''
        draft=false
      fi
      git push --quiet --set-upstream origin "$branch" || back_to_draft "Pushing $branch failed."
      say "pushed $branch"
      [ "$draft" != true ] || make_ready
    elif [ -z "$url" ]; then
      make_ready
    fi
    say "PR #$number is ready at $sha; waiting for CI and CodeQL"

    outcome=''
    await_run ci.yml "$sha" "$since" Changes
    ci="$outcome"
    await_run codeql.yml "$sha" "$since" 'Analyze (C#)'
    codeql="$outcome"
    if [ "${ci%% *}" != success ] || [ "${codeql%% *}" != success ]; then
      back_to_draft "CI: $ci. CodeQL: $codeql"
    fi
    failing=''
    await_required "$number"
    [ -z "$failing" ] || back_to_draft "Required checks: $failing"
    say 'every required check passed'
  fi

  state=$(gh_retry pr view "$number" --json mergeStateStatus | jq -r .mergeStateStatus) || back_to_draft 'Its merge state could not be read.'
  [ "$state" != BEHIND ] || back_to_draft 'master moved while CI ran; run land.sh again to merge it in.'
  if ! merge_pr "$number" "$sha"; then
    [ "$state" != BEHIND ] || back_to_draft 'master moved while CI ran; run land.sh again to merge it in.'
    back_to_draft "GitHub refused the merge (merge state $state, gh's reason above)."
  fi
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
