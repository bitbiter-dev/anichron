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

# A fresh repository per case, committed on `base`: logic files, a view, a
# test, a declaration file and a catalog.
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
  out=$(cd "$repo" && "$script" --base base --print "${@:4}" 2>&1); status=$?
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
check 'changed logic runs incrementally' "$repo" ''

repo=$(fixture view)
echo 'export const v = 2;' > "$repo/src/Anichron.UI/src/app/View.tsx"
git -C "$repo" rm -q src/Anichron.UI/src/shared/a.test.ts
check 'a changed view or deleted test runs incrementally' "$repo" ''

repo=$(fixture config)
echo '{ "mutate": ["src/**/*.ts"] }' > "$repo/src/Anichron.UI/stryker.config.json"
echo '{}' > "$repo/src/Anichron.UI/package.json"
check 'a changed measurement setup runs every mutant' "$repo" \
  $'running every mutant; the measurement setup changed: package.json stryker.config.json\n--force'

repo=$(fixture lockfile)
echo 'lock' > "$repo/src/Anichron.UI/pnpm-lock.yaml"
echo 'export {};' > "$repo/src/Anichron.UI/vite.config.ts"
check 'a changed lockfile or Vite config runs every mutant' "$repo" \
  $'running every mutant; the measurement setup changed: pnpm-lock.yaml vite.config.ts\n--force'

repo=$(fixture all)
check '--all runs every mutant with no changes' "$repo" '--force' --all

repo=$(fixture nobase)
out=$(cd "$repo" && "$script" --base missing --print 2>&1); status=$?
if [ "$status" -eq 2 ]; then pass 'an unknown base exits 2'; else fail 'an unknown base exits 2' 'exit 2' "exit $status with: $out"; fi

# Fakes: pnpm records Stryker's arguments and exits $1; gh serves the newest
# master artifact as a zip holding $2, or fails when $2 is empty.
fakes() {
  mkdir -p "$work/bin"
  cat > "$work/bin/pnpm" <<SH
#!/usr/bin/env bash
echo "stryker:\${*:3}"
exit $1
SH
  cat > "$work/bin/gh" <<SH
#!/usr/bin/env bash
[ -n '$2' ] || exit 1
case "\$2" in
  *'/artifacts?'*) echo 42 ;;
  */artifacts/42/zip) d=\$(mktemp -d); echo '$2' > "\$d/stryker-incremental.json"; (cd "\$d" && zip -q - stryker-incremental.json) ;;
  *) exit 1 ;;
esac
SH
  chmod +x "$work/bin/pnpm" "$work/bin/gh"
}

run_check() {
  local label="$1" repo="$2" want_status="$3" needle="$4" out status
  out=$(cd "$repo" && PATH="$work/bin:$PATH" WITH_LOCK_POLL_SECONDS=1 "$script" --base base 2>&1); status=$?
  if [ "$status" -ne "$want_status" ]; then
    fail "$label" "exit $want_status" "exit $status with: $out"
  elif [[ "$out" != *"$needle"* ]]; then
    fail "$label" "output containing '$needle'" "$out"
  else
    pass "$label"
  fi
}

repo=$(fixture seeded)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
fakes 0 '{"from":"master"}'
run_check 'a missing baseline is seeded from master' "$repo" 0 "seeded the incremental baseline from master's artifact 42"
seeded=$(cat "$repo/src/Anichron.UI/reports/stryker-incremental.json" 2>/dev/null)
if [ "$seeded" = '{"from":"master"}' ]; then pass 'the seed lands where Stryker reads it'; else fail 'the seed lands where Stryker reads it' '{"from":"master"}' "$seeded"; fi

fakes 0 '{"from":"elsewhere"}'
run_check 'a local baseline is kept' "$repo" 0 'stryker:'
seeded=$(cat "$repo/src/Anichron.UI/reports/stryker-incremental.json")
if [ "$seeded" = '{"from":"master"}' ]; then pass 'a local baseline is not replaced'; else fail 'a local baseline is not replaced' '{"from":"master"}' "$seeded"; fi

repo=$(fixture unseeded)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
fakes 0 ''
run_check 'no artifact runs every mutant' "$repo" 0 'no incremental baseline from master; every mutant runs'

repo=$(fixture failing)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
fakes 2 ''
run_check 'a run below the threshold fails with its status' "$repo" 2 'stryker:'

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi
echo 'all passed'
