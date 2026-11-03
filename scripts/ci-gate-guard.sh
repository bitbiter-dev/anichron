#!/usr/bin/env bash
# Usage: scripts/ci-gate-guard.sh [workflow] (default .github/workflows/ci.yml)
#
# Fails when the gate's --backend list drifts from the jobs that skip with the
# backend (scripts/ci-gate.sh, scripts/changed-areas.sh):
#   - every job in the gate's needs whose job-level `if` requires
#     needs.changes.outputs.backend == 'true' is passed to ci-gate.sh as --backend;
#   - every job passed as --backend has such an `if`;
#   - every job with such an `if` has `changes` in its needs, without which the
#     output reads as empty and the job never runs.
#
# Exit 0: all hold. Exit 1: violations, one per line on stdout. Exit 2: the
# workflow cannot be read or has no `gate` job.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
workflow="${1:-$here/../.github/workflows/ci.yml}"

if [ ! -r "$workflow" ]; then
  echo "cannot read $workflow" >&2
  exit 2
fi

# One line per job, `job <name> <backend-if: 0|1> <need>...`, and one per
# --backend argument in the gate job, `arg <name>`.
parsed=$(awk -v backend_if="needs\\.changes\\.outputs\\.backend == [\"']true[\"']" '
  function flush() { if (job != "") print "job", job, backend, needs }
  /^jobs:/ { in_jobs = 1; next }
  in_jobs && /^[^ #]/ { in_jobs = 0 }
  !in_jobs { next }
  /^  [A-Za-z0-9_-]+:[ \t]*$/ {
    flush()
    job = $1; sub(/:$/, "", job)
    backend = 0; needs = ""; key = ""
    next
  }
  job == "" { next }
  /^    [A-Za-z0-9_-]+:/ {
    key = $1; sub(/:$/, "", key)
    if (key == "needs") {
      value = $0
      sub(/^    needs:[ \t]*/, "", value)
      gsub(/[][,]/, " ", value)
      needs = value
    }
    if (key == "if" && $0 ~ backend_if) backend = 1
    if (job == "gate") gate_line($0)
    next
  }
  /^      / || /^[ \t]*$/ {
    if (key == "needs" && $1 == "-") needs = needs " " $2
    if (key == "if" && $0 ~ backend_if) backend = 1
    if (job == "gate") gate_line($0)
    next
  }
  function gate_line(line,   n, i, words) {
    n = split(line, words, /[ \t]+/)
    for (i = 1; i < n; i++) if (words[i] == "--backend") print "arg", words[i + 1]
  }
  END { flush() }
' "$workflow")

if ! grep -q '^job gate ' <<<"$parsed"; then
  echo "$workflow has no gate job" >&2
  exit 2
fi

gate_needs=$(awk '$1 == "job" && $2 == "gate" { for (i = 4; i <= NF; i++) print $i }' <<<"$parsed")
args=$(awk '$1 == "arg" { print $2 }' <<<"$parsed")
violations=()

while read -r _ name backend needs; do
  [ "$backend" = 1 ] || continue
  if ! tr ' ' '\n' <<<"$needs" | grep -qx changes; then
    violations+=("job '$name' reads needs.changes.outputs.backend but does not need 'changes'")
  fi
  if grep -qx "$name" <<<"$gate_needs" && ! grep -qx "$name" <<<"$args"; then
    violations+=("job '$name' skips with the backend but the gate does not pass it as --backend")
  fi
done < <(grep '^job ' <<<"$parsed")

while read -r name; do
  [ -n "$name" ] || continue
  if ! grep -q "^job $name 1" <<<"$parsed"; then
    violations+=("the gate passes '$name' as --backend but its if does not require needs.changes.outputs.backend == 'true'")
  fi
done <<<"$args"

if [ "${#violations[@]}" -gt 0 ]; then
  printf '%s\n' "${violations[@]}"
  exit 1
fi
