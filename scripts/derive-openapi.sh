#!/usr/bin/env bash
# Regenerates docs/openapi/v1.json — the OpenAPI document of the API's HTTP surface.
#
# usage: scripts/derive-openapi.sh [--check]
#   --check   regenerate beside the file and FAIL if it differs. This is what CI runs.
#
# The generator (src/Anichron.OpenApiGenerator) composes only the surface and never starts a
# host, so this needs no database, no Docker and no secrets.
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"

target="docs/openapi/v1.json"
check=false
[ "${1:-}" = "--check" ] && check=true

mkdir -p "$(dirname "$target")"

derive() {
  dotnet run --project src/Anichron.OpenApiGenerator --no-launch-profile -v q -- "$1" >/dev/null
}

if [ "$check" = true ]; then
  derived="$(dirname "$target")/.$(basename "$target").derived"
  trap 'rm -f "$derived"' EXIT
  derive "$root/$derived"

  if ! drift=$(diff -u "$target" "$derived" 2>&1); then
    printf '%s is stale.\n\n' "$target" >&2
    printf '%s\n' "$drift" >&2
    printf '\nRun scripts/derive-openapi.sh and commit the result.\n' >&2
    exit 1
  fi

  printf '%s is current.\n' "$target"
  exit 0
fi

derive "$root/$target"
printf '%s regenerated from the API surface.\n' "$target"

if command -v git >/dev/null 2>&1 && git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  if [ -z "$(git status --porcelain -- "$target")" ]; then
    printf 'No change — the committed file already matched the API.\n'
  else
    printf 'The file CHANGED. Review and commit it:\n  git add %s && git commit\n' "$target"
  fi
fi
