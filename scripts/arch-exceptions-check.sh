#!/usr/bin/env bash
# Usage: scripts/arch-exceptions-check.sh [exceptions-json]
#
# Fails when an architecture-rule exception names an issue that is already
# closed: the PR that closed it should have deleted the exception. Needs `gh`
# authenticated for the repository (GH_TOKEN in CI). See
# docs/agents/design-process.md, "Adding an architecture rule".
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
exceptions="${1:-$root/src/Anichron.Architecture.Tests.Unit/architecture-exceptions.json}"

if ! issues=$(jq -r '.[].issue' "$exceptions" | sort -un); then
  echo "cannot read exceptions from $exceptions" >&2
  exit 2
fi

closed=()
while IFS= read -r issue; do
  [ -n "$issue" ] || continue
  state=$(gh issue view "$issue" --json state --jq .state)
  if [ "$state" != "OPEN" ]; then
    closed+=("$issue")
  fi
done <<<"$issues"

if [ "${#closed[@]}" -gt 0 ]; then
  echo 'architecture exceptions still name closed issues; delete their entries:' >&2
  for issue in "${closed[@]}"; do
    jq -r --argjson issue "$issue" \
      '.[] | select(.issue == $issue) | "  #\(.issue)  \(.rule)  \(.type)"' "$exceptions" >&2
  done
  exit 1
fi

echo "every architecture exception names an open issue"
