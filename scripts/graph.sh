#!/usr/bin/env bash
# Usage: scripts/graph.sh <explain|affected|path|query|...> [args ...]
#
# Runs a graphify command against this worktree's code graph, building or
# refreshing graphify-out/graph.json first when it is missing or older than any
# file under src/, docs/, scripts/ or CLAUDE.md. The graph is local only (it is
# gitignored), so each worktree builds its own; a full build takes seconds and
# calls no LLM.
set -euo pipefail

if [ "$#" -eq 0 ] || [ "$1" = '-h' ] || [ "$1" = '--help' ]; then
  sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'
  exit 0
fi

if ! command -v graphify >/dev/null 2>&1; then
  echo 'graphify is not installed: uv tool install graphifyy (or pipx install graphifyy)' >&2
  exit 127
fi

cd "$(git rev-parse --show-toplevel)"
graph=graphify-out/graph.json

stale() {
  [ ! -f "$graph" ] && return 0
  [ -n "$(find src docs scripts CLAUDE.md -type f -newer "$graph" \
    -not -path '*/bin/*' -not -path '*/obj/*' -print -quit 2>/dev/null)" ]
}

if stale; then
  log=$(mktemp "${TMPDIR:-/tmp}/graph-build.XXXXXXXX")
  trap 'rm -f "$log"' EXIT
  if ! graphify update . --force >"$log" 2>&1; then
    echo 'graphify update failed:' >&2
    tail -20 "$log" >&2
    exit 1
  fi
fi

graphify "$@"
