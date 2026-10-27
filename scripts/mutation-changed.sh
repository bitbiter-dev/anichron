#!/usr/bin/env bash
# Usage: scripts/mutation-changed.sh [--base <ref>] [--print] [source-file ...]
#
# Runs Stryker over only the C# files changed since the merge-base with <ref>
# (default origin/master): committed, staged, unstaged and untracked. A changed
# test file <Name><Suffix>Tests.cs in <Project>.Tests.Unit selects, in <Project>,
# the file <Name><Suffix>.cs, else the file declaring that type, else the file
# named after the longest leading CamelCase part of it (ProxySweepEndpointsHttp
# finds ProxySweepEndpoints.cs). A test project with no production project
# (Anichron.Architecture.Tests.Unit) is not mutated. Test changes that map to no
# source file exit 1 unless the source files they cover are passed explicitly,
# as paths relative to src/.
#
# Before Stryker, the unit suite runs once: Stryker tests every mutant against
# every test project, so one failing test fails each project's run. A failing
# suite exits 1 with no Stryker run.
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
    -h|--help) sed -n '2,26p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
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
outside_tests=()

# The source file in $1 (a project folder under src/) that the test class $2 covers, if exactly one.
subject_of() {
  local project="$1" name="$2" match stem
  match=$(cd "$src" && find "$project" -name "$name.cs" -not -path '*/obj/*' -not -path '*/bin/*')
  if [ -z "$match" ]; then
    match=$(cd "$src" && grep -rlE --include='*.cs' --exclude-dir=obj --exclude-dir=bin \
      "(class|record|struct|interface)[[:space:]]+$name([^[:alnum:]_]|\$)" "$project" || true)
  fi
  stem="$name"
  while [ -z "$match" ]; do
    stem=$(sed -E 's/[A-Z][a-z0-9]*$//' <<<"$stem")
    [ -n "$stem" ] || break
    match=$(cd "$src" && find "$project" -name "$stem.cs" -not -path '*/obj/*' -not -path '*/bin/*')
  done
  if [ -n "$match" ] && [ "$(wc -l <<<"$match")" -eq 1 ]; then
    printf '%s' "$match"
  fi
}

while IFS= read -r path; do
  [ -n "$path" ] || continue
  rel="${path#src/}"
  case "$rel" in
    *.Tests.Unit/*)
      project="${rel%%.Tests.Unit/*}"
      file=$(basename "$rel")
      name="${file%Tests.cs}"
      match=''
      if [ ! -d "$src/$project" ]; then
        outside_tests+=("$rel")
        continue
      fi
      if [ "$name" != "$file" ]; then
        match=$(subject_of "$project" "$name")
      fi
      if [ -n "$match" ]; then
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

if [ "${#outside_tests[@]}" -gt 0 ]; then
  echo "not mutated (tests of no production project): ${outside_tests[*]}"
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
echo '== unit tests'
if ! dotnet test; then
  echo '== unit tests failed: fix them before mutating; Stryker would fail every run' >&2
  exit 1
fi

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
