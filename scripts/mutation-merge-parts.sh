#!/usr/bin/env bash
# Usage: scripts/mutation-merge-parts.sh <projects-json> <parts-dir> <out-dir>
#
# Merges the per-project reports of a mutation matrix into <out-dir>: the
# merged mutation-report.json and the viewer as mutation-report.html.
# <projects-json> is the plan's projects array (mutation-changed.sh --matrix);
# each project's report is <parts-dir>/mutation-part-<name>/mutation-report.json.
#
# Exit 0: merged. Exit 1: no plan, a project left no report, or the merge
# refused (scripts/mutation-report.sh, "merge"); nothing is written then.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
projects="$1" parts="$2" out="$3"

if [ -z "$projects" ]; then
  echo "::error::No mutation target list to merge against"
  exit 1
fi

missing=$(jq -r '.[].name' <<<"$projects" | while IFS= read -r name; do
  [ -f "$parts/mutation-part-$name/mutation-report.json" ] || echo "$name"
done)
if [ -n "$missing" ]; then
  echo "::error::No mutation report from: $(echo $missing)"
  exit 1
fi

reports=()
while IFS= read -r name; do
  reports+=("$parts/mutation-part-$name/mutation-report.json")
done < <(jq -r '.[].name' <<<"$projects")

mkdir -p "$out"
"$here/mutation-report.sh" merge "${reports[@]}" > "$out/mutation-report.json.tmp" \
  || { rm -f "$out/mutation-report.json.tmp"; exit 1; }
mv "$out/mutation-report.json.tmp" "$out/mutation-report.json"
cp "$here/mutation-viewer.html" "$out/mutation-report.html"
