#!/usr/bin/env bash
# Usage: scripts/ui-mutation-guard.sh [repo-root]
#
# The UI's counterpart of mutation-guard.sh. Fails when the StrykerJS setup in
# src/Anichron.UI drifts from the rules in CLAUDE.md ("The UI") and ADR 0014:
#   - stryker.config.json keeps testRunner "vitest", thresholds of 100, no
#     incremental mode and no ignoreStatic;
#   - its mutate list starts from "src/**/*.ts", and every other entry (an
#     exclusion, or a view file brought back into scope) is named in ADR 0014;
#   - no workflow, hook, script or package.json script passes a flag that
#     changes what is measured (--incremental, --force, --ignoreStatic, ...);
#   - --mutate/-m appears only in ui-mutation-changed.sh;
#   - workflows run StrykerJS only through scripts/ui-mutation-changed.sh;
#   - every UI file with a `// Stryker disable` directive is named in ADR 0014.
#
# Exit 0: all hold. Exit 1: violations, one per line on stdout. Exit 2: the
# config or the ADR cannot be read.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="${1:-$(cd "$here/.." && pwd)}"
ui="src/Anichron.UI"
config="$root/$ui/stryker.config.json"
adr=$(ls "$root"/docs/adr/0014-*.md 2>/dev/null | head -n 1 || true)
self="scripts/$(basename "${BASH_SOURCE[0]}")"

if [ ! -r "$config" ] || [ -z "$adr" ] || ! jq -e . "$config" >/dev/null 2>&1; then
  echo "cannot read $ui/stryker.config.json or docs/adr/0014-*.md under $root" >&2
  exit 2
fi

violations=()

runner=$(jq -r '.testRunner // "unset"' "$config")
[ "$runner" = vitest ] || violations+=("stryker.config.json: testRunner is \"$runner\", must be \"vitest\"")

for key in break low high; do
  value=$(jq -r --arg key "$key" '.thresholds[$key] // "unset"' "$config")
  [ "$value" = 100 ] || violations+=("stryker.config.json: thresholds.$key is $value, must be 100")
done

for key in incremental ignoreStatic; do
  [ "$(jq -r --arg key "$key" '.[$key] // false' "$config")" = false ] \
    || violations+=("stryker.config.json: $key is set; it hides mutants from the gate")
done

[ "$(jq -r '.mutate[0] // "unset"' "$config")" = 'src/**/*.ts' ] \
  || violations+=('stryker.config.json: mutate must start with "src/**/*.ts", the logic files')

adr_text=$(cat "$adr")

while IFS= read -r entry; do
  [ -n "$entry" ] || continue
  [[ "$adr_text" == *"${entry#!}"* ]] || violations+=("stryker.config.json: mutate entry $entry is not named in ADR 0014")
done < <(jq -r '.mutate[1:][]?' "$config")

# Workflows, hook config, helper scripts and the UI's package scripts: everything that can invoke StrykerJS.
callers=()
while IFS= read -r file; do
  callers+=("$file")
done < <(cd "$root" && {
  find .github/workflows scripts .husky -type f \
    \( -name '*.yml' -o -name '*.yaml' -o -name '*.sh' -o -name '*.json' \) \
    ! -name '*.test.sh' ! -path "$self" ! -path 'scripts/fixtures/*' 2>/dev/null || true
  [ ! -f "$ui/package.json" ] || echo "$ui/package.json"
} | sort)

flag_in() {
  local pattern="$1" file
  for file in "${callers[@]+"${callers[@]}"}"; do
    grep -Eq -- "$pattern" "$root/$file" && echo "$file"
  done
  return 0
}

measuring='incremental|incrementalFile|force|ignoreStatic|ignorePatterns|testFiles|testRunner|coverageAnalysis|allowEmpty|inPlace|files'
while IFS= read -r file; do
  [ -n "$file" ] && violations+=("$file: passes a StrykerJS flag that changes what is measured; settings live in stryker.config.json")
done < <(flag_in "stryker.*(^|[[:space:]])--($measuring)([[:space:]=\"']|\$)")

while IFS= read -r file; do
  [ -n "$file" ] && [ "$file" != scripts/ui-mutation-changed.sh ] \
    && violations+=("$file: passes --mutate to StrykerJS, sanctioned only in ui-mutation-changed.sh")
done < <(flag_in "stryker[[:space:]]+run.*(^|[[:space:]])(--mutate|-m)([[:space:]=\"']|\$)")

while IFS= read -r file; do
  [ -n "$file" ] && violations+=("$file: runs StrykerJS directly; workflows go through scripts/ui-mutation-changed.sh")
done < <(flag_in '(stryker[[:space:]]+run|pnpm([[:space:]]+run)?[[:space:]]+mutate)' | grep '^\.github/workflows/' || true)

while IFS= read -r file; do
  [ -n "$file" ] || continue
  [[ "$adr_text" == *"${file#"$ui/"}"* ]] || violations+=("$file: has a Stryker directive, but ADR 0014 does not name it")
done < <(cd "$root" && grep -rl --include='*.ts' --include='*.tsx' --exclude-dir=node_modules \
  'Stryker disable' "$ui/src" 2>/dev/null | sort)

if [ "${#violations[@]}" -gt 0 ]; then
  printf '%s\n' "${violations[@]}"
  exit 1
fi
echo "UI mutation setup matches its rules"
