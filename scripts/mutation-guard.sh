#!/usr/bin/env bash
# Usage: scripts/mutation-guard.sh [repo-root]
#
# Fails when the mutation-testing setup drifts from the rules in CLAUDE.md
# ("Mutation testing") and docs/mutation-testing.md:
#   - stryker-config.json keeps test-runner "mtp" and thresholds of 100;
#   - no workflow or script passes --since, --with-baseline or a threshold flag;
#   - --concurrency appears only in mutation-divergence.yml, and --mutate/-m
#     only in mutation-changed.sh;
#   - every file with a `// Stryker disable` directive, and every path excluded
#     in stryker-config.json's mutate list, is named in ADR 0005.
#
# Exit 0: all hold. Exit 1: violations, one per line on stdout. Exit 2: the
# config or the ADR cannot be read.
set -euo pipefail

root="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
config="$root/src/stryker-config.json"
adr=$(ls "$root"/docs/adr/0005-*.md 2>/dev/null | head -n 1 || true)
self="scripts/$(basename "${BASH_SOURCE[0]}")"

if [ ! -r "$config" ] || [ -z "$adr" ]; then
  echo "cannot read src/stryker-config.json or docs/adr/0005-*.md under $root" >&2
  exit 2
fi

violations=()

runner=$(jq -r '."stryker-config"."test-runner" // "unset"' "$config")
[ "$runner" = mtp ] || violations+=("stryker-config.json: test-runner is \"$runner\", must be \"mtp\" (ADR 0001)")

for key in break low high; do
  value=$(jq -r --arg key "$key" '."stryker-config".thresholds[$key] // "unset"' "$config")
  [ "$value" = 100 ] || violations+=("stryker-config.json: thresholds.$key is $value, must be 100")
done

# Workflows, hook config and helper scripts: everything that can invoke Stryker.
callers=()
while IFS= read -r file; do
  callers+=("$file")
done < <(cd "$root" && find .github/workflows scripts .husky -type f \
  \( -name '*.yml' -o -name '*.yaml' -o -name '*.sh' -o -name '*.json' \) \
  ! -name '*.test.sh' ! -path "$self" ! -path 'scripts/fixtures/*' 2>/dev/null | sort)

flag_in() {
  local pattern="$1" file
  for file in "${callers[@]+"${callers[@]}"}"; do
    grep -Eq -- "$pattern" "$root/$file" && echo "$file"
  done
  return 0
}

while IFS= read -r file; do
  [ -n "$file" ] && violations+=("$file: passes --since or --with-baseline, which test 0 mutants in a worktree")
done < <(flag_in '(^|[[:space:]])--(since|with-baseline)([[:space:]=:]|$)')

while IFS= read -r file; do
  [ -n "$file" ] && violations+=("$file: passes a threshold flag; the gate is stryker-config.json's thresholds")
done < <(flag_in '(^|[[:space:]])--(break-at|threshold-low|threshold-high)([[:space:]=:]|$)')

while IFS= read -r file; do
  [ -n "$file" ] && [ "$file" != .github/workflows/mutation-divergence.yml ] \
    && violations+=("$file: passes --concurrency, sanctioned only in mutation-divergence.yml")
done < <(flag_in '(^|[[:space:]])--concurrency([[:space:]=:]|$)')

while IFS= read -r file; do
  [ -n "$file" ] && [ "$file" != scripts/mutation-changed.sh ] \
    && violations+=("$file: passes --mutate, sanctioned only in mutation-changed.sh")
done < <(flag_in 'stryker.*(^|[[:space:]])(--mutate|-m)([[:space:]=]|$)')

adr_text=$(cat "$adr")

while IFS= read -r path; do
  [ -n "$path" ] || continue
  [[ "$adr_text" == *"$path"* ]] || violations+=("stryker-config.json excludes $path, which ADR 0005 does not name")
done < <(jq -r '."stryker-config".mutate[]? | select(startswith("!")) | sub("^!(\\*\\*/)?"; "")' "$config")

while IFS= read -r file; do
  [ -n "$file" ] || continue
  named=false
  while IFS= read -r type; do
    [[ "$adr_text" == *"$type"* ]] && { named=true; break; }
  done < <(grep -oE '(class|record|struct|interface)[[:space:]]+[A-Z][A-Za-z0-9]*' "$root/$file" | awk '{print $2}')
  $named || violations+=("$file: has a Stryker directive, but ADR 0005 names none of its types")
done < <(cd "$root" && grep -rl --include='*.cs' 'Stryker disable' src 2>/dev/null | grep -v '\.Tests\.Unit/' | sort)

if [ "${#violations[@]}" -gt 0 ]; then
  printf '%s\n' "${violations[@]}"
  exit 1
fi
echo "mutation setup matches its rules"
