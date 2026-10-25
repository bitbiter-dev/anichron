#!/usr/bin/env bash
# Usage: scripts/mutation-targets.sh [--opted-out] [src-dir]
#
# Lists the projects of <src-dir>/Anichron.slnx (default: src/) that Stryker
# mutates, one csproj path relative to <src-dir> per line. A project is a
# target when MSBuild evaluates IsMutationTarget to true (Directory.Build.props
# sets it for every non-test project). --opted-out lists the non-test projects
# that set it to false instead.
#
# Exit 0: listed (possibly nothing). Exit 2: the solution cannot be read or a
# project cannot be evaluated.
set -euo pipefail

mode=targets
src=''
while [ "$#" -gt 0 ]; do
  case "$1" in
    --opted-out) mode=opted-out; shift ;;
    -h|--help) sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) src="$1"; shift ;;
  esac
done
src="${src:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../src" && pwd)}"
solution="$src/Anichron.slnx"

if [ ! -r "$solution" ]; then
  echo "cannot read $solution" >&2
  exit 2
fi

grep -oE '<Project Path="[^"]+"' "$solution" | sed -E 's/^<Project Path="(.*)"$/\1/' | sort |
  while IFS= read -r project; do
    if ! props=$(cd "$src" && dotnet msbuild "$project" -getProperty:IsMutationTarget -getProperty:IsTestCodeProject 2>&1); then
      echo "cannot evaluate $project: $props" >&2
      exit 2
    fi
    target=$(jq -r '.Properties.IsMutationTarget | ascii_downcase' <<<"$props")
    test_code=$(jq -r '.Properties.IsTestCodeProject | ascii_downcase' <<<"$props")
    case "$mode" in
      targets) [ "$target" = true ] && echo "$project" ;;
      opted-out) [ "$target" != true ] && [ "$test_code" != true ] && echo "$project" ;;
    esac
    true
  done
