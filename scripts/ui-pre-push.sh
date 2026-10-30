#!/usr/bin/env bash
# Usage: scripts/ui-pre-push.sh [--base <ref>]
#
# The pre-push hook's UI step. Runs the UI's tests when HEAD changes anything
# under src/Anichron.UI since the merge-base with <ref> (default origin/master),
# after a frozen install so a fresh worktree has its dependencies. Otherwise
# skips. pnpm provides Node itself (devEngines.runtime in package.json).
set -euo pipefail

base='origin/master'

while [ "$#" -gt 0 ]; do
  case "$1" in
    --base) base="${2:?--base needs a ref}"; shift 2 ;;
    -h|--help) sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

cd "$(git rev-parse --show-toplevel)"

if ! merge_base=$(git merge-base "$base" HEAD 2>/dev/null); then
  echo "cannot find a merge-base with '$base'; fetch it or pass --base <ref>" >&2
  exit 2
fi

if git diff --quiet "$merge_base" HEAD -- src/Anichron.UI; then
  echo "no UI change since $base; skipping the UI tests"
  exit 0
fi

pnpm --dir src/Anichron.UI install --frozen-lockfile
pnpm --dir src/Anichron.UI test
