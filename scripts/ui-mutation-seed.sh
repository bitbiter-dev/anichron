#!/usr/bin/env bash
# Usage: scripts/ui-mutation-seed.sh
#
# Downloads the UI's incremental baseline from master: of the artifacts
# `ui-mutation-incremental` uploaded by master runs, the one whose commit (the
# run's head_sha) is the newest ancestor of HEAD. It lands in
# src/Anichron.UI/reports/stryker-incremental.json, its commit in
# stryker-incremental.commit, and the commit is printed.
# Exit 1: no such artifact, or it cannot be fetched (needs `gh`).
set -euo pipefail

root=$(git rev-parse --show-toplevel)
reports="$root/src/Anichron.UI/reports"
artifact='ui-mutation-incremental'

best='' best_id='' best_depth=-1
while read -r id sha; do
  git -C "$root" merge-base --is-ancestor "$sha" HEAD 2>/dev/null || continue
  depth=$(git -C "$root" rev-list --count "$sha")
  if [ "$depth" -gt "$best_depth" ]; then
    best=$sha best_id=$id best_depth=$depth
  fi
done < <(gh api "repos/{owner}/{repo}/actions/artifacts?name=$artifact&per_page=100" 2>/dev/null \
  | jq -r '.artifacts[] | select(.workflow_run.head_branch == "master" and (.expired | not))
      | "\(.id) \(.workflow_run.head_sha)"' 2>/dev/null || true)

[ -n "$best" ] || exit 1

seed=$(mktemp -d "${TMPDIR:-/tmp}/ui-mutation-seed.XXXXXXXX")
trap 'rm -rf "$seed"' EXIT
gh api "repos/{owner}/{repo}/actions/artifacts/$best_id/zip" >"$seed/baseline.zip" 2>/dev/null \
  && unzip -qo "$seed/baseline.zip" -d "$seed" && [ -f "$seed/stryker-incremental.json" ] \
  || exit 1

mkdir -p "$reports"
mv "$seed/stryker-incremental.json" "$reports/stryker-incremental.json"
echo "$best" >"$reports/stryker-incremental.commit"
echo "$best"
