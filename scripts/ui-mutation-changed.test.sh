#!/usr/bin/env bash
# Tests for ui-mutation-changed.sh. Run: scripts/ui-mutation-changed.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/ui-mutation-changed.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

work=$(mktemp -d "${TMPDIR:-/tmp}/ui-mutation-changed-test.XXXXXXXX") \
  || { echo 'could not create a temp dir' >&2; exit 1; }
trap 'rm -rf "$work"' EXIT

# A fresh repository per case, committed on `base`: two logic files and a view
# in scope, and a test, a declaration file and a catalog out of it.
fixture() {
  local repo="$work/$1" ui="$work/$1/src/Anichron.UI"
  mkdir -p "$ui/src/app" "$ui/src/shared" "$ui/src/i18n/locales"
  cat > "$ui/stryker.config.json" <<'JSON'
{ "mutate": ["src/**/*.ts", "src/app/router.tsx", "!src/**/*.test.ts", "!src/**/*.d.ts", "!src/i18n/locales/**"] }
JSON
  echo 'export const a = 1;' > "$ui/src/shared/a.ts"
  echo 'export const b = 1;' > "$ui/src/shared/b.ts"
  echo 'export const r = 1;' > "$ui/src/app/router.tsx"
  echo 'export const v = 1;' > "$ui/src/app/View.tsx"
  echo 'export {};' > "$ui/src/shared/a.test.ts"
  echo 'export {};' > "$ui/src/shared/types.d.ts"
  echo '{}' > "$ui/src/i18n/locales/en.json"
  echo 'readme' > "$repo/README.md"
  git -C "$repo" init -q -b base
  git -C "$repo" add -A
  git -C "$repo" -c user.name=t -c user.email=t@t commit -qm base
  printf '%s' "$repo"
}

check() {
  local label="$1" repo="$2" want="$3" out status
  out=$(cd "$repo" && "$script" --base base --print 2>&1); status=$?
  if [ "$status" -ne 0 ]; then
    fail "$label" "exit 0 with '$want'" "exit $status with: $out"
  elif [ "$out" != "$want" ]; then
    fail "$label" "$want" "$out"
  else
    pass "$label"
  fi
}

echo 'ui-mutation-changed.sh'

repo=$(fixture none)
echo 'changed' >> "$repo/README.md"
check 'a change outside the UI mutates nothing' "$repo" \
  'nothing changed under src/Anichron.UI since base; nothing to mutate'

repo=$(fixture logic)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
echo 'export const c = 1;' > "$repo/src/Anichron.UI/src/shared/c.ts"
check 'changed and new logic files are mutated on their own' "$repo" \
  $'--mutate\nsrc/shared/a.ts,src/shared/c.ts'

repo=$(fixture router)
echo 'export const r = 2;' > "$repo/src/Anichron.UI/src/app/router.tsx"
check 'the route tree is in scope although it is a view file' "$repo" \
  $'--mutate\nsrc/app/router.tsx'

repo=$(fixture test)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
echo 'export {}; // more' > "$repo/src/Anichron.UI/src/shared/a.test.ts"
check 'a changed test mutates the whole scope' "$repo" \
  'mutating the whole UI scope; changed outside it: src/shared/a.test.ts'

repo=$(fixture view)
echo 'export const v = 2;' > "$repo/src/Anichron.UI/src/app/View.tsx"
check 'a changed view mutates the whole scope' "$repo" \
  'mutating the whole UI scope; changed outside it: src/app/View.tsx'

repo=$(fixture config)
echo '{ "mutate": ["src/**/*.ts"] }' > "$repo/src/Anichron.UI/stryker.config.json"
check 'a changed config mutates the whole scope' "$repo" \
  'mutating the whole UI scope; changed outside it: stryker.config.json'

repo=$(fixture all)
out=$(cd "$repo" && "$script" --all --print 2>&1)
if [ -z "$out" ]; then pass '--all mutates the whole scope with no changes'; else fail '--all mutates the whole scope with no changes' '(no arguments)' "$out"; fi

repo=$(fixture nobase)
out=$(cd "$repo" && "$script" --base missing --print 2>&1); status=$?
if [ "$status" -eq 2 ]; then pass 'an unknown base exits 2'; else fail 'an unknown base exits 2' 'exit 2' "exit $status with: $out"; fi

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi
echo 'all passed'
