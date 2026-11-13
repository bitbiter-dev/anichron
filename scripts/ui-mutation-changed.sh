#!/usr/bin/env bash
# Usage: scripts/ui-mutation-changed.sh [--base <ref>] [--all] [--print]
#
# Runs StrykerJS over the UI's whole mutation scope (src/Anichron.UI) in
# incremental mode: a mutant whose code and covering tests are unchanged since
# the baseline (reports/stryker-incremental.json) keeps its recorded result.
#   - No UI file changed since the merge-base with <ref> (default
#     origin/master; committed, staged, unstaged, untracked, deleted): nothing
#     to mutate, exit 0.
#   - A changed measurement setup (stryker.config.json, vite.config.ts,
#     package.json, pnpm-lock.yaml): every mutant runs again (--force).
#   - Anything else: an incremental run. Without a local baseline, it is seeded
#     from the newest master artifact `ui-mutation-incremental` (needs `gh`);
#     without one, every mutant runs.
# --all runs every mutant regardless (--force) and writes a fresh baseline.
# --print prints the Stryker arguments instead of running it. Stryker runs
# under the heavy lock (scripts/with-lock.sh).
#
# See docs/mutation-testing.md, "The UI".
set -euo pipefail

base='origin/master'
all=false
print=false

while [ "$#" -gt 0 ]; do
  case "$1" in
    --base) base="${2:?--base needs a ref}"; shift 2 ;;
    --all) all=true; shift ;;
    --print) print=true; shift ;;
    -h|--help) sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root=$(git rev-parse --show-toplevel)
ui="$root/src/Anichron.UI"
baseline="$ui/reports/stryker-incremental.json"
artifact='ui-mutation-incremental'
cd "$root"

args=()
if $all; then
  args=(--force)
else
  if ! merge_base=$(git merge-base "$base" HEAD 2>/dev/null); then
    echo "cannot find a merge-base with '$base'; fetch it or pass --base <ref>" >&2
    exit 2
  fi

  changed=$(
    {
      git diff --name-only "$merge_base" -- src/Anichron.UI
      git ls-files --others --exclude-standard -- src/Anichron.UI
    } | sed 's#^src/Anichron.UI/##' | sort -u
  )

  if [ -z "$changed" ]; then
    echo "nothing changed under src/Anichron.UI since $base; nothing to mutate"
    exit 0
  fi

  setup=$(grep -xE 'stryker\.config\.json|vite\.config\.ts|package\.json|pnpm-lock\.yaml' <<<"$changed" || true)
  if [ -n "$setup" ]; then
    echo "running every mutant; the measurement setup changed: $(tr '\n' ' ' <<<"$setup" | sed 's/ $//')"
    args=(--force)
  fi
fi

if $print; then
  [ "${#args[@]}" -eq 0 ] || printf '%s\n' "${args[@]}"
  exit 0
fi

if [ "${#args[@]}" -eq 0 ] && [ ! -f "$baseline" ]; then
  seed=$(mktemp -d "${TMPDIR:-/tmp}/ui-mutation-seed.XXXXXXXX")
  id=$(gh api "repos/{owner}/{repo}/actions/artifacts?name=$artifact&per_page=30" \
    --jq '[.artifacts[] | select(.workflow_run.head_branch == "master" and (.expired | not))][0].id // empty' \
    2>/dev/null || true)
  if [ -n "$id" ] && gh api "repos/{owner}/{repo}/actions/artifacts/$id/zip" >"$seed/baseline.zip" 2>/dev/null \
    && unzip -qo "$seed/baseline.zip" -d "$seed" && [ -f "$seed/stryker-incremental.json" ]; then
    mkdir -p "$(dirname "$baseline")"
    mv "$seed/stryker-incremental.json" "$baseline"
    echo "seeded the incremental baseline from master's artifact $id"
  else
    echo "no incremental baseline from master; every mutant runs"
  fi
  rm -rf "$seed"
fi

cd "$ui"
"$here/with-lock.sh" heavy pnpm exec stryker run "${args[@]+"${args[@]}"}"
