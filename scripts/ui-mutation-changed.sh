#!/usr/bin/env bash
# Usage: scripts/ui-mutation-changed.sh [--base <ref>] [--all] [--print | --setup]
#
# Runs StrykerJS over the UI's whole mutation scope (src/Anichron.UI) in
# incremental mode: a mutant whose code and covering tests are unchanged since
# the baseline (reports/stryker-incremental.json) keeps its recorded result.
# The commit the baseline was measured on sits next to it
# (reports/stryker-incremental.commit).
#   - No UI file changed since the merge-base with <ref> (default
#     origin/master; committed, staged, unstaged, untracked, deleted): nothing
#     to mutate, exit 0.
#   - A local baseline whose commit is unknown is discarded. Without a local
#     baseline, one is seeded from master (scripts/ui-mutation-seed.sh);
#     without one, every mutant runs.
#   - Otherwise an incremental run, also when the measurement setup (the
#     `setup` list below, printed by --setup) changed since the baseline's
#     commit: it says so, and the master push re-measures every mutant
#     (scripts/ui-mutation-promote.sh).
# --all runs every mutant regardless (--force) and writes a fresh baseline.
# A run that writes the baseline records HEAD as its commit, or no commit when
# the setup differs from HEAD. --print prints the Stryker arguments instead of
# running it. Stryker runs under the heavy lock (scripts/with-lock.sh), after
# clearing sandboxes an aborted run left behind (its static-leaks reporter
# exits mid-run).
#
# See docs/mutation-testing.md, "The UI".
set -euo pipefail

base='origin/master'
all=false
print=false
setup_only=false

while [ "$#" -gt 0 ]; do
  case "$1" in
    --base) base="${2:?--base needs a ref}"; shift 2 ;;
    --all) all=true; shift ;;
    --print) print=true; shift ;;
    --setup) setup_only=true; shift ;;
    -h|--help) sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root=$(git rev-parse --show-toplevel)
ui="$root/src/Anichron.UI"
baseline="$ui/reports/stryker-incremental.json"
commit_file="$ui/reports/stryker-incremental.commit"
setup=(src/Anichron.UI/stryker.config.json src/Anichron.UI/vite.config.ts src/Anichron.UI/package.json src/Anichron.UI/pnpm-lock.yaml)
cd "$root"

if $setup_only; then
  printf '%s\n' "${setup[@]}"
  exit 0
fi

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

  commit=''
  if [ -f "$baseline" ]; then
    commit=$(cat "$commit_file" 2>/dev/null || true)
    if [ -z "$commit" ] || ! git cat-file -e "$commit^{commit}" 2>/dev/null; then
      echo "discarding the local baseline; the commit it was measured on is unknown"
      rm -f "$baseline" "$commit_file"
      commit=''
    fi
  fi

  if [ ! -f "$baseline" ]; then
    if commit=$("$here/ui-mutation-seed.sh"); then
      echo "seeded the incremental baseline from master's artifact at $commit"
    else
      commit=''
      echo "no incremental baseline from master; every mutant runs"
    fi
  fi

  if [ -n "$commit" ]; then
    drift=$(
      {
        git diff --name-only "$commit" -- "${setup[@]}"
        git ls-files --others --exclude-standard -- "${setup[@]}"
      } | sed 's#^src/Anichron.UI/##' | sort -u
    )
    if [ -n "$drift" ]; then
      echo "the measurement setup changed since the baseline's commit: $(tr '\n' ' ' <<<"$drift" | sed 's/ $//'); incremental here, the master push re-measures every mutant"
    fi
  fi
fi

if $print; then
  [ "${#args[@]}" -eq 0 ] || printf '%s\n' "${args[@]}"
  exit 0
fi

stamp=$(mktemp "${TMPDIR:-/tmp}/ui-mutation-stamp.XXXXXXXX")
trap 'rm -f "$stamp"' EXIT
touch -t 197001010001 "$stamp"
[ ! -f "$baseline" ] || touch -t 197001010000 "$baseline"

status=0
(
  cd "$ui"
  "$here/with-lock.sh" heavy sh -c 'rm -rf "$1" && shift && exec "$@"' sh \
    "$(jq -r '.tempDirName // ".stryker-tmp"' stryker.config.json)" \
    pnpm exec stryker run "${args[@]+"${args[@]}"}"
) || status=$?

if [ "$baseline" -nt "$stamp" ]; then
  if [ -z "$(git status --porcelain -- "${setup[@]}")" ]; then
    git rev-parse HEAD >"$commit_file"
  else
    rm -f "$commit_file"
  fi
fi
exit "$status"
