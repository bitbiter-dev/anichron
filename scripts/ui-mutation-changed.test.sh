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

git_as_mira() { git -c user.name=mira -c user.email=mira@example.org "$@"; }

# A fresh repository per case, committed on `base`: logic files, a view, a
# test, a declaration file, a catalog and the measurement setup.
fixture() {
  local repo="$work/$1" ui="$work/$1/src/Anichron.UI"
  mkdir -p "$ui/src/app" "$ui/src/shared" "$ui/src/i18n/locales"
  cat > "$ui/stryker.config.json" <<'JSON'
{ "mutate": ["src/**/*.ts", "src/app/router.tsx", "!src/**/*.test.ts", "!src/**/*.d.ts", "!src/i18n/locales/**"] }
JSON
  echo '{}' > "$ui/package.json"
  echo 'export const a = 1;' > "$ui/src/shared/a.ts"
  echo 'export const b = 1;' > "$ui/src/shared/b.ts"
  echo 'export const r = 1;' > "$ui/src/app/router.tsx"
  echo 'export const v = 1;' > "$ui/src/app/View.tsx"
  echo 'export {};' > "$ui/src/shared/a.test.ts"
  echo 'export {};' > "$ui/src/shared/types.d.ts"
  echo '{}' > "$ui/src/i18n/locales/en.json"
  echo 'reports/' > "$ui/.gitignore"
  echo 'readme' > "$repo/README.md"
  git -C "$repo" init -q -b base
  git -C "$repo" add -A
  git_as_mira -C "$repo" commit -qm base
  mkdir -p "$work/state/$1"
  printf '%s' "$repo"
}

commit_all() { git -C "$1" add -A && git_as_mira -C "$1" commit -qm "$2"; }

# A local baseline measured on <commit>, or with no recorded commit when it is
# empty.
local_baseline() {
  local reports="$1/src/Anichron.UI/reports"
  mkdir -p "$reports"
  echo '{"from":"local"}' > "$reports/stryker-incremental.json"
  if [ -n "$2" ]; then echo "$2" > "$reports/stryker-incremental.commit"; fi
}

# Master's artifacts: <id> <head_sha> <content> triples, newest first.
artifacts() {
  local state="$work/state/$(basename "$1")" list='' sep=''
  shift
  while [ "$#" -gt 0 ]; do
    list+="$sep"$(printf '{"id":%s,"expired":false,"workflow_run":{"head_branch":"master","head_sha":"%s"}}' "$1" "$2")
    echo "$3" > "$state/$1.json"
    sep=,
    shift 3
  done
  echo "{\"artifacts\":[$list]}" > "$state/ui-mutation-incremental.list"
}

# Fakes: gh from scripts/fixtures/fake-gh; pnpm records Stryker's arguments, writes
# a baseline unless $PNPM_WRITES is 0, and exits $PNPM_STATUS.
mkdir -p "$work/bin"
cat > "$work/bin/pnpm" <<'SH'
#!/usr/bin/env bash
echo "stryker:${*:3}"
if [ "${PNPM_WRITES:-1}" = 1 ]; then mkdir -p reports && echo '{"from":"run"}' > reports/stryker-incremental.json; fi
exit "${PNPM_STATUS:-0}"
SH
chmod +x "$work/bin/pnpm"

invoke() {
  local repo="$1"
  shift
  (cd "$repo" && PATH="$here/fixtures/fake-gh:$work/bin:$PATH" GH_STATE="$work/state/$(basename "$repo")" \
    WITH_LOCK_POLL_SECONDS=1 "$script" --base base "$@" 2>&1)
}

check() {
  local label="$1" repo="$2" want="$3" out status
  out=$(invoke "$repo" --print "${@:4}"); status=$?
  if [ "$status" -ne 0 ]; then
    fail "$label" "exit 0 with '$want'" "exit $status with: $out"
  elif [ "$out" != "$want" ]; then
    fail "$label" "$want" "$out"
  else
    pass "$label"
  fi
}

run_check() {
  local label="$1" repo="$2" want_status="$3" needle="$4" out status
  out=$(invoke "$repo"); status=$?
  if [ "$status" -ne "$want_status" ]; then
    fail "$label" "exit $want_status" "exit $status with: $out"
  elif [[ "$out" != *"$needle"* ]]; then
    fail "$label" "output containing '$needle'" "$out"
  else
    pass "$label"
  fi
}

file_is() {
  local label="$1" file="$2" want="$3" actual
  actual=$(cat "$file" 2>/dev/null || echo '<missing>')
  if [ "$actual" = "$want" ]; then pass "$label"; else fail "$label" "$want" "$actual"; fi
}

head_of() { git -C "$1" rev-parse HEAD; }

echo 'ui-mutation-changed.sh'

repo=$(fixture none)
echo 'changed' >> "$repo/README.md"
check 'a change outside the UI mutates nothing' "$repo" \
  'nothing changed under src/Anichron.UI since base; nothing to mutate'

repo=$(fixture logic)
local_baseline "$repo" "$(head_of "$repo")"
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
echo 'export const c = 1;' > "$repo/src/Anichron.UI/src/shared/c.ts"
check 'changed logic runs incrementally' "$repo" ''

repo=$(fixture view)
local_baseline "$repo" "$(head_of "$repo")"
echo 'export const v = 2;' > "$repo/src/Anichron.UI/src/app/View.tsx"
git -C "$repo" rm -q src/Anichron.UI/src/shared/a.test.ts
check 'a changed view or deleted test runs incrementally' "$repo" ''

repo=$(fixture config)
local_baseline "$repo" "$(head_of "$repo")"
echo '{ "mutate": ["src/**/*.ts"] }' > "$repo/src/Anichron.UI/stryker.config.json"
echo '{"name":"ui"}' > "$repo/src/Anichron.UI/package.json"
check 'a measurement setup changed since the baseline runs incrementally and names it' "$repo" \
  $'the measurement setup changed since the baseline\'s commit: package.json stryker.config.json; incremental here, the master push re-measures every mutant'

repo=$(fixture lockfile)
local_baseline "$repo" "$(head_of "$repo")"
echo 'lock' > "$repo/src/Anichron.UI/pnpm-lock.yaml"
echo 'export {};' > "$repo/src/Anichron.UI/vite.config.ts"
check 'a new lockfile or Vite config runs incrementally and names them' "$repo" \
  $'the measurement setup changed since the baseline\'s commit: pnpm-lock.yaml vite.config.ts; incremental here, the master push re-measures every mutant'

repo=$(fixture behind)
measured=$(head_of "$repo")
echo '{"name":"ui"}' > "$repo/src/Anichron.UI/package.json"
commit_all "$repo" 'master moves on'
git -C "$repo" checkout -q -b topic
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
local_baseline "$repo" "$measured"
check 'a setup change between the baseline and the merge-base runs incrementally' "$repo" \
  $'the measurement setup changed since the baseline\'s commit: package.json; incremental here, the master push re-measures every mutant'

repo=$(fixture measured)
git -C "$repo" checkout -q -b topic
echo '{"name":"ui"}' > "$repo/src/Anichron.UI/package.json"
commit_all "$repo" 'setup'
local_baseline "$repo" "$(head_of "$repo")"
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
check 'a setup change the baseline was measured on runs incrementally' "$repo" ''

repo=$(fixture unknown)
local_baseline "$repo" ''
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
check 'a baseline with no recorded commit is discarded' "$repo" \
  $'discarding the local baseline; the commit it was measured on is unknown\nno incremental baseline from master; every mutant runs'
[ ! -e "$repo/src/Anichron.UI/reports/stryker-incremental.json" ] \
  && pass 'the discarded baseline is gone' || fail 'the discarded baseline is gone' 'no file' 'a file'

repo=$(fixture foreign)
local_baseline "$repo" 0123456789abcdef0123456789abcdef01234567
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
artifacts "$repo" 42 "$(head_of "$repo")" '{"from":"master"}'
check 'a baseline whose commit is not here is replaced from master' "$repo" \
  $'discarding the local baseline; the commit it was measured on is unknown\nseeded the incremental baseline from master\'s artifact at '"$(head_of "$repo")"
file_is 'the replacement is master'\''s' "$repo/src/Anichron.UI/reports/stryker-incremental.json" '{"from":"master"}'

repo=$(fixture setup)
check '--setup prints the measurement setup' "$repo" \
  $'src/Anichron.UI/stryker.config.json\nsrc/Anichron.UI/vite.config.ts\nsrc/Anichron.UI/package.json\nsrc/Anichron.UI/pnpm-lock.yaml' --setup

repo=$(fixture all)
check '--all runs every mutant with no changes' "$repo" '--force' --all

repo=$(fixture nobase)
out=$(cd "$repo" && "$script" --base missing --print 2>&1); status=$?
if [ "$status" -eq 2 ]; then pass 'an unknown base exits 2'; else fail 'an unknown base exits 2' 'exit 2' "exit $status with: $out"; fi

repo=$(fixture seeded)
first=$(head_of "$repo")
echo 'export const b = 2;' > "$repo/src/Anichron.UI/src/shared/b.ts"
commit_all "$repo" second
second=$(head_of "$repo")
echo '{"name":"ui"}' > "$repo/src/Anichron.UI/package.json"
git -C "$repo" checkout -q -b topic
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
artifacts "$repo" \
  9 0123456789abcdef0123456789abcdef01234567 '{"from":"unknown"}' \
  8 "$first" '{"from":"first"}' \
  7 "$second" '{"from":"second"}'
check 'a seed from master is the newest ancestor, and its commit drives the setup check' "$repo" \
  $'seeded the incremental baseline from master\'s artifact at '"$second"$'\nthe measurement setup changed since the baseline\'s commit: package.json; incremental here, the master push re-measures every mutant'
file_is 'the seed lands where Stryker reads it' "$repo/src/Anichron.UI/reports/stryker-incremental.json" '{"from":"second"}'
file_is 'the seed records its commit' "$repo/src/Anichron.UI/reports/stryker-incremental.commit" "$second"

repo=$(fixture kept)
local_baseline "$repo" "$(head_of "$repo")"
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
artifacts "$repo" 42 "$(head_of "$repo")" '{"from":"master"}'
PNPM_WRITES=0 run_check 'a local baseline is kept' "$repo" 0 'stryker:'
file_is 'a local baseline is not replaced' "$repo/src/Anichron.UI/reports/stryker-incremental.json" '{"from":"local"}'

repo=$(fixture unseeded)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
run_check 'no artifact runs every mutant' "$repo" 0 'no incremental baseline from master; every mutant runs'
file_is 'a run records HEAD as its baseline'\''s commit' "$repo/src/Anichron.UI/reports/stryker-incremental.commit" "$(head_of "$repo")"

repo=$(fixture dirty)
local_baseline "$repo" "$(head_of "$repo")"
echo '{"name":"ui"}' > "$repo/src/Anichron.UI/package.json"
run_check 'a run on an uncommitted setup' "$repo" 0 'stryker:run'
file_is 'records no commit' "$repo/src/Anichron.UI/reports/stryker-incremental.commit" '<missing>'

repo=$(fixture crashed)
before=$(head_of "$repo")
local_baseline "$repo" "$before"
git -C "$repo" checkout -q -b topic
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
commit_all "$repo" change
PNPM_WRITES=0 PNPM_STATUS=1 run_check 'a run that writes no baseline' "$repo" 1 'stryker:'
file_is 'keeps the baseline'\''s commit' "$repo/src/Anichron.UI/reports/stryker-incremental.commit" "$before"

repo=$(fixture stale)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
mkdir -p "$repo/src/Anichron.UI/.stryker-tmp/sandbox-old"
run_check 'a run clears sandboxes an aborted run left' "$repo" 0 'stryker:'
if [ ! -e "$repo/src/Anichron.UI/.stryker-tmp" ]; then pass 'the stale sandbox is gone'; else fail 'the stale sandbox is gone' 'no .stryker-tmp' "$(ls "$repo/src/Anichron.UI/.stryker-tmp")"; fi

repo=$(fixture failing)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/shared/a.ts"
PNPM_STATUS=2 run_check 'a run below the threshold fails with its status' "$repo" 2 'stryker:'

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi
echo 'all passed'
