#!/usr/bin/env bash
# Usage: <changed paths, one per line> | scripts/changed-areas.sh <event-name>
#
# Prints `backend=true` or `backend=false`, for $GITHUB_OUTPUT. The backend is
# unchanged only on a pull request whose changed paths all lie under
# src/Anichron.UI/ or are Markdown outside src/, except ADR 0005, which
# scripts/mutation-guard.sh reads. Any other path, an empty list, and every
# other event count as a backend change, so an unclassified file can never
# skip a job.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo 'usage: <changed paths> | changed-areas.sh <event-name>' >&2
  exit 2
fi

event="$1"
backend=true

if [ "$event" = pull_request ]; then
  paths=$(cat)
  if [ -n "$paths" ] && ! awk '
    /^src\/Anichron\.UI\// { next }
    /\.md$/ && !/^src\// && !/^docs\/adr\/0005-/ { next }
    { backend = 1 }
    END { exit !backend }' <<<"$paths"; then
    backend=false
  fi
fi

echo "backend=$backend"
