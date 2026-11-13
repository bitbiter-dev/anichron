#!/usr/bin/env bash
# Usage: ci-gate.sh <event-name> <needs-json> [--backend <job>]... [pull-request-only-job ...]
#
# Exits 0 when every job in <needs-json> (the workflow's `toJSON(needs)`)
# succeeded. A pull-request-only job may also be skipped, but only when the
# event is not a pull request. A --backend job may also be skipped, but only
# when the `changes` job (scripts/changed-areas.sh) reported `backend` as
# "false". Any job but `changes` may be skipped when `changes` reported
# `tested` as "true" (scripts/tested-on-pr.sh). Anything else exits 1 and names
# each job at fault.
set -euo pipefail

usage() {
  echo 'usage: ci-gate.sh <event-name> <needs-json> [--backend <job>]... [pull-request-only-job ...]' >&2
  exit 2
}

if [ "$#" -lt 2 ]; then
  usage
fi

event="$1"
needs="$2"
shift 2

backend_jobs=()
while [ "$#" -gt 0 ] && [ "$1" = --backend ]; do
  if [ "$#" -lt 2 ]; then
    usage
  fi
  backend_jobs+=("$2")
  shift 2
done

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

for job in ${backend_jobs[@]+"${backend_jobs[@]}"}; do
  if ! jq -e --arg job "$job" 'has($job)' >/dev/null <<<"$needs"; then
    echo "backend job '$job' is not in needs" >&2
    exit 2
  fi
done

if [ "${#backend_jobs[@]}" -gt 0 ] && ! jq -e 'has("changes")' >/dev/null <<<"$needs"; then
  echo "backend jobs need the 'changes' job in needs" >&2
  exit 2
fi

as_json_list() { printf '%s\n' "$@" | jq -R . | jq -sc 'map(select(. != ""))'; }
pr_only=$(as_json_list "$@")
backend=$(as_json_list ${backend_jobs[@]+"${backend_jobs[@]}"})

blocked=$(jq -r --arg event "$event" --argjson pr_only "$pr_only" --argjson backend "$backend" '
  (.changes.outputs.backend == "false") as $backend_unchanged
  | (.changes.outputs.tested == "true") as $tested
  | to_entries[]
  | .key as $job
  | select(
      .value.result != "success"
      and ((.value.result == "skipped" and $event != "pull_request" and ($pr_only | index($job) != null)) | not)
      and ((.value.result == "skipped" and $backend_unchanged and ($backend | index($job) != null)) | not)
      and ((.value.result == "skipped" and $tested and $job != "changes") | not)
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
