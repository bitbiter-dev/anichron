#!/usr/bin/env bash
# Usage: scripts/arch-review-due.sh [threshold]
#
# Counts the first-parent commits on HEAD since the latest `arch-review/*` tag,
# leaving out Dependabot's. At <threshold> (default 10) or more, opens an
# "Architecture review due" issue labelled ready-for-agent, or updates the count
# in the title of the one already open. Needs `gh` authenticated (GH_TOKEN in
# CI) and the tags fetched. The review procedure is in
# docs/agents/design-process.md, "Architecture review".
set -euo pipefail

threshold="${1:-10}"
title_prefix='Architecture review due'

tag=$(git tag --list 'arch-review/*' --sort=-refname | head -n 1)
if [ -z "$tag" ]; then
  echo 'no arch-review/* tag found; fetch tags, or tag the last reviewed commit' >&2
  exit 2
fi

count=$(git log --first-parent --format='%an' "$tag..HEAD" | grep -cv '^dependabot\[bot\]$' || true)
title="$title_prefix ($count PRs since $tag)"

if [ "$count" -lt "$threshold" ]; then
  echo "$count PRs since $tag; a review is due at $threshold"
  exit 0
fi

open=$(gh issue list --state open --search "in:title \"$title_prefix\"" --json number,title \
  --jq "[.[] | select(.title | startswith(\"$title_prefix\"))][0].number // empty")

if [ -n "$open" ]; then
  gh issue edit "$open" --title "$title"
  echo "updated #$open: $title"
  exit 0
fi

gh issue create --title "$title" --label ready-for-agent --body-file - <<EOF
$count PRs have merged since \`$tag\`, the last architecture review.

## What to build

The architecture review of \`$tag..master\`, following "Architecture review" in \`docs/agents/design-process.md\`: findings re-verified against master, filed as issues, placed next in the milestone orders, and the reviewed commit tagged.

## First caller

The maintainer, who decides on the findings, and the next issues picked from the milestone orders.

## Design

Design: not needed because the review procedure is already defined in \`docs/agents/design-process.md\`.
EOF
echo "opened: $title"
