#!/usr/bin/env bash
# Usage: ci-gate.sh <event-name> <needs-json> [pull-request-only-job ...]
#
# Exits 0 when every job in <needs-json> (the workflow's `toJSON(needs)`)
# succeeded. A pull-request-only job may also be skipped, but only when the
# event is not a pull request. Anything else exits 1 and names each job at fault.
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo 'usage: ci-gate.sh <event-name> <needs-json> [pull-request-only-job ...]' >&2
  exit 2
fi

event="$1"
needs="$2"
shift 2

if ! jq -e 'type == "object"' >/dev/null 2>&1 <<<"$needs"; then
  echo 'needs is not a JSON object' >&2
  exit 2
fi

if [ "$(jq 'length' <<<"$needs")" -eq 0 ]; then
  echo 'needs lists no jobs; the gate would pass vacuously' >&2
  exit 2
fi

for job in "$@"; do
  if ! jq -e --arg job "$job" 'has($job)' >/dev/null <<<"$needs"; then
    echo "pull-request-only job '$job' is not in needs" >&2
    exit 2
  fi
done

pr_only=$(printf '%s\n' "$@" | jq -R . | jq -sc 'map(select(. != ""))')

blocked=$(jq -r --arg event "$event" --argjson pr_only "$pr_only" '
  to_entries[]
  | .key as $job
  | select(
      .value.result != "success"
      and ((.value.result == "skipped" and $event != "pull_request" and ($pr_only | index($job) != null)) | not)
    )
  | "\(.key): \(.value.result)"
' <<<"$needs")

jq -r 'to_entries[] | "\(.key): \(.value.result)"' <<<"$needs"

if [ -n "$blocked" ]; then
  echo
  echo 'blocked by:' >&2
  printf '  %s\n' "$blocked" >&2
  exit 1
fi
