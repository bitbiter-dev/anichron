#!/usr/bin/env bash
# Usage: scripts/mutation-changed.sh [--base <ref>] [--print] [source-file ...]
#
# Runs Stryker over only the C# files changed since the merge-base with <ref>
# (default origin/master): committed, staged, unstaged and untracked. A changed
# test file <Name>Tests.cs selects <Name>.cs in the matching production project.
# Test changes that map to no source file exit 1 unless the source files they
# cover are passed explicitly, as paths relative to src/.
#
# A local pre-check only; the full sweep in CI is the authority. See
# docs/mutation-testing.md, "The scoped local run".
set -euo pipefail

base='origin/master'
print=false
explicit=()

while [ "$#" -gt 0 ]; do
  case "$1" in
    --base) base="${2:?--base needs a ref}"; shift 2 ;;
    --print) print=true; shift ;;
    -h|--help) sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) explicit+=("${1#src/}"); shift ;;
  esac
done

root=$(git rev-parse --show-toplevel)
src="$root/src"
cd "$root"

if ! merge_base=$(git merge-base "$base" HEAD 2>/dev/null); then
  echo "cannot find a merge-base with '$base'; fetch it or pass --base <ref>" >&2
  exit 2
fi

changed=$(
  {
    git diff --name-only --diff-filter=d "$merge_base" -- 'src/*.cs'
    git ls-files --others --exclude-standard -- 'src/*.cs'
  } | sort -u
)

targets=()
unmapped=()

while IFS= read -r path; do
  [ -n "$path" ] || continue
  rel="${path#src/}"
  case "$rel" in
    *.Tests.Unit/*)
      project="${rel%%.Tests.Unit/*}"
      file=$(basename "$rel")
      name="${file%Tests.cs}"
      match=''
      if [ "$name" != "$file" ] && [ -d "$src/$project" ]; then
        match=$(cd "$src" && find "$project" -name "$name.cs" -not -path '*/obj/*' -not -path '*/bin/*')
      fi
      if [ -n "$match" ] && [ "$(wc -l <<<"$match")" -eq 1 ]; then
        targets+=("$match")
      else
        unmapped+=("$rel")
      fi
      ;;
    *) targets+=("$rel") ;;
  esac
done <<<"$changed"

for file in "${explicit[@]+"${explicit[@]}"}"; do
  if [ ! -f "$src/$file" ]; then
    echo "explicit source file not found under src/: $file" >&2
    exit 2
  fi
  targets+=("$file")
done

if [ "${#unmapped[@]}" -gt 0 ]; then
  echo 'changed test files that name no single source file:' >&2
  printf '  %s\n' "${unmapped[@]}" >&2
  if [ "${#explicit[@]}" -eq 0 ]; then
    echo 'pass the source files they cover, relative to src/, e.g.' >&2
    echo '  scripts/mutation-changed.sh Anichron.API/Endpoints/AdminUserEndpoints.cs' >&2
    exit 1
  fi
fi

if [ "${#targets[@]}" -eq 0 ]; then
  echo "nothing changed under src/ since $base; nothing to mutate"
  exit 0
fi

args=()
while IFS= read -r target; do
  args+=(-m "**/$target")
done < <(printf '%s\n' "${targets[@]}" | sort -u)
while IFS= read -r glob; do
  args+=(-m "$glob")
done < <(jq -r '."stryker-config".mutate[]' "$src/stryker-config.json")

if $print; then
  printf '%s\n' "${args[@]}"
  exit 0
fi

cd "$src"
exec dotnet stryker "${args[@]}"
