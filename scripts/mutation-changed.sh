#!/usr/bin/env bash
# Usage: scripts/mutation-changed.sh [--base <ref>] [--print] [source-file ...]
#
# Runs Stryker over only the C# files changed since the merge-base with <ref>
# (default origin/master): committed, staged, unstaged and untracked. A changed
# test file <Name>Tests.cs selects <Name>.cs in the matching production project.
# Test changes that map to no source file exit 1 unless the source files they
# cover are passed explicitly, as paths relative to src/.
#
# One Stryker run per mutation target project (scripts/mutation-targets.sh),
# one after another, each with its own TMPDIR. Every run goes ahead; the exit
# status is the worst of them. Files outside every target project are named
# and skipped.
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
    -h|--help) sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) explicit+=("${1#src/}"); shift ;;
  esac
done

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
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

projects=$("$here/mutation-targets.sh" "$src") || exit 2
files=$(printf '%s\n' "${targets[@]}" | sort -u)

exclusions=()
while IFS= read -r glob; do
  exclusions+=(-m "$glob")
done < <(jq -r '."stryker-config".mutate[]' "$src/stryker-config.json")

outside="$files"
runs=()
while IFS= read -r project; do
  [ -n "$project" ] || continue
  dir="${project%/*}/"
  [ -n "$(awk -v dir="$dir" 'index($0, dir) == 1' <<<"$files")" ] || continue
  outside=$(awk -v dir="$dir" 'index($0, dir) != 1' <<<"$outside")
  runs+=("$project")
done <<<"$projects"

if [ -n "$outside" ]; then
  echo "not mutated (in no mutation target project): $(tr '\n' ' ' <<<"$outside" | sed 's/ $//')"
fi

if [ "${#runs[@]}" -eq 0 ]; then
  echo 'no changed file is in a mutation target project; nothing to mutate'
  exit 0
fi

args_for() {
  local project="$1" dir="${1%/*}/" target
  printf '%s\n' --project "$(basename "$project")"
  while IFS= read -r target; do
    printf '%s\n' -m "**/$target"
  done < <(awk -v dir="$dir" 'index($0, dir) == 1' <<<"$files")
  printf '%s\n' "${exclusions[@]+"${exclusions[@]}"}"
}

if $print; then
  for project in "${runs[@]}"; do
    args_for "$project"
  done
  exit 0
fi

run_tmp=$(mktemp -d "${TMPDIR:-/tmp}/mutation-changed.XXXXXXXX") \
  || { echo 'could not create a private temp dir' >&2; exit 2; }
trap 'rm -rf "$run_tmp"' EXIT

cd "$src"
worst=0
for project in "${runs[@]}"; do
  name=$(basename "$project" .csproj)
  args=()
  while IFS= read -r arg; do
    args+=("$arg")
  done < <(args_for "$project")
  mkdir "$run_tmp/$name"
  echo "== $name"
  status=0
  TMPDIR="$run_tmp/$name" dotnet stryker "${args[@]}" || status=$?
  echo "== $name: exit $status"
  [ "$status" -le "$worst" ] || worst=$status
done
exit "$worst"
