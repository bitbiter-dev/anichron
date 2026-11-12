#!/usr/bin/env bash
# Usage: scripts/tested-on-pr.sh <sha>
#
# Prints `tested=true` when <sha> is the merge commit of a pull request whose
# head has the same tree and passed `CI gate`, else `tested=false`, for
# $GITHUB_OUTPUT. Any lookup that fails counts as untested. The reason goes to
# stderr. Needs `gh` authenticated (GH_TOKEN).
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo 'usage: tested-on-pr.sh <sha>' >&2
  exit 2
fi
sha="$1"

untested() {
  echo "$1" >&2
  echo 'tested=false'
  exit 0
}

tree=$(git rev-parse --verify --quiet "$sha^{tree}") || untested "cannot read the tree of $sha"

pr=$(gh api "repos/{owner}/{repo}/commits/$sha/pulls" 2>/dev/null \
  | jq -r --arg sha "$sha" '[.[] | select(.merge_commit_sha == $sha)][0] // empty | "\(.number) \(.head.sha)"') \
  || untested "cannot list the pull requests of $sha"
[ -n "$pr" ] || untested "$sha is not the merge commit of a pull request"
read -r number head <<<"$pr"

head_tree=$(gh api "repos/{owner}/{repo}/git/commits/$head" 2>/dev/null | jq -r '.tree.sha') \
  || untested "cannot read the head of #$number"
[ "$head_tree" = "$tree" ] || untested "#$number's head $head has another tree than $sha"

gate=$(gh api "repos/{owner}/{repo}/commits/$head/check-runs?check_name=CI%20gate" 2>/dev/null \
  | jq -r '[.check_runs[] | select(.status == "completed")] | sort_by(.completed_at) | last | .conclusion // empty') \
  || untested "cannot read the checks of #$number"
[ "$gate" = success ] || untested "#$number's last CI gate on $head is '${gate:-missing}'"

echo "#$number's head $head has this tree and passed CI gate" >&2
echo 'tested=true'
