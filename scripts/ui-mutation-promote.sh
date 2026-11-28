#!/usr/bin/env bash
# Usage: scripts/ui-mutation-promote.sh
#
# For a master push whose tree passed on its pull request: puts the UI
# incremental baseline describing HEAD in src/Anichron.UI/reports/, for CI to
# publish as `ui-mutation-incremental`. Prints, for $GITHUB_OUTPUT:
#   baseline=remeasure the push changed the measurement setup
#                      (ui-mutation-changed.sh --setup) since HEAD^: a full
#                      run re-measures every mutant, whatever the PR measured;
#   baseline=promoted  the pull request's file, the artifact
#                      `ui-mutation-pr-<tree>` measured on HEAD's tree;
#   baseline=carried   master's baseline (scripts/ui-mutation-seed.sh), whose
#                      commit has the same UI files as HEAD;
#   baseline=stale     that baseline, plus commit=<its commit>: an incremental
#                      run from that commit brings it up to HEAD;
#   baseline=missing   no baseline: a full run makes one.
# The reason goes to stderr. Needs `gh` authenticated (GH_TOKEN).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root=$(git rev-parse --show-toplevel)
reports="$root/src/Anichron.UI/reports"
tree=$(git -C "$root" rev-parse 'HEAD^{tree}')
artifact="ui-mutation-pr-$tree"

finish() {
  echo "$1" >&2
  echo "baseline=$2"
  exit 0
}

mapfile -t setup < <("$here/ui-mutation-changed.sh" --setup)
[ "${#setup[@]}" -gt 0 ] || { echo 'ui-mutation-changed.sh --setup listed nothing' >&2; exit 1; }
if git -C "$root" rev-parse -q --verify 'HEAD^' >/dev/null \
  && ! git -C "$root" diff --quiet 'HEAD^' HEAD -- "${setup[@]}"; then
  finish "the push changed the measurement setup; a full run re-measures every mutant" remeasure
fi

id=$(gh api "repos/{owner}/{repo}/actions/artifacts?name=$artifact&per_page=100" 2>/dev/null \
  | jq -r '[.artifacts[] | select(.expired | not)][0].id // empty' 2>/dev/null || true)
if [ -n "$id" ]; then
  promote=$(mktemp -d "${TMPDIR:-/tmp}/ui-mutation-promote.XXXXXXXX")
  trap 'rm -rf "$promote"' EXIT
  if gh api "repos/{owner}/{repo}/actions/artifacts/$id/zip" >"$promote/baseline.zip" 2>/dev/null \
    && unzip -qo "$promote/baseline.zip" -d "$promote" && [ -f "$promote/stryker-incremental.json" ]; then
    mkdir -p "$reports"
    mv "$promote/stryker-incremental.json" "$reports/stryker-incremental.json"
    finish "promoted the pull request's artifact $artifact" promoted
  fi
  echo "cannot fetch the pull request's artifact $artifact" >&2
fi

commit=$("$here/ui-mutation-seed.sh") || finish "no $artifact and no baseline from master" missing

if git -C "$root" diff --quiet "$commit" HEAD -- src/Anichron.UI; then
  finish "no $artifact; master's baseline at $commit has the same UI files" carried
fi
echo "commit=$commit"
finish "no $artifact; master's baseline at $commit is behind the UI files" stale
