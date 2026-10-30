#!/usr/bin/env bash
# Usage: scripts/ui-mutation-changed.sh [--base <ref>] [--all] [--print]
#
# Runs StrykerJS over the UI (src/Anichron.UI) for the files changed since the
# merge-base with <ref> (default origin/master): committed, staged, unstaged,
# untracked and deleted.
#   - No UI file changed: nothing to mutate, exit 0.
#   - Every changed UI file is in the mutation scope (stryker.config.json's
#     mutate list): only those files are mutated.
#   - Anything else changed (a test, a view, a catalog, test support, config,
#     dependencies, a deleted file): the whole scope is mutated.
# --all mutates the whole scope regardless. --print prints the Stryker
# arguments instead of running it (none means the whole scope).
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
    -h|--help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

root=$(git rev-parse --show-toplevel)
ui="$root/src/Anichron.UI"
cd "$root"

args=()
if ! $all; then
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

  # The mutate list's files, as Stryker resolves them.
  scope=$(cd "$ui" && node -e '
    const fs = require("node:fs");
    const { mutate } = JSON.parse(fs.readFileSync("stryker.config.json", "utf8"));
    const include = mutate.filter((p) => !p.startsWith("!"));
    const exclude = mutate.filter((p) => p.startsWith("!")).map((p) => p.slice(1));
    for (const file of fs.globSync(include, { exclude })) console.log(file);
  ' | sort -u)

  outside=$(comm -23 <(echo "$changed") <(echo "$scope"))
  if [ -z "$outside" ]; then
    args=(--mutate "$(paste -sd, - <<<"$changed")")
  else
    echo "mutating the whole UI scope; changed outside it: $(tr '\n' ' ' <<<"$outside" | sed 's/ $//')"
  fi
fi

if $print; then
  [ "${#args[@]}" -eq 0 ] || printf '%s\n' "${args[@]}"
  exit 0
fi

cd "$ui"
pnpm exec stryker run "${args[@]+"${args[@]}"}"
