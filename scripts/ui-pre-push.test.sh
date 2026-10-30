#!/usr/bin/env bash
# Tests for ui-pre-push.sh. Run: scripts/ui-pre-push.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/ui-pre-push.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

work=$(mktemp -d "${TMPDIR:-/tmp}/ui-pre-push-test.XXXXXXXX") \
  || { echo 'could not create a temp dir' >&2; exit 1; }
trap 'rm -rf "$work"' EXIT

# A fake pnpm that logs its arguments and exits with $PNPM_STATUS for `test`.
mkdir -p "$work/bin"
cat > "$work/bin/pnpm" <<'SH'
#!/usr/bin/env bash
echo "pnpm $*" >> "$PNPM_LOG"
[ "${*: -1}" = test ] && exit "${PNPM_STATUS:-0}"
exit 0
SH
chmod +x "$work/bin/pnpm"

# A fresh repository per case, committed on `base`, with HEAD on a branch from it.
fixture() {
  local repo="$work/$1"
  mkdir -p "$repo/src/Anichron.UI/src" "$repo/scripts"
  echo 'export const a = 1;' > "$repo/src/Anichron.UI/src/a.ts"
  echo 'echo hi' > "$repo/scripts/x.sh"
  git -C "$repo" init -q -b base
  git -C "$repo" add -A
  git -C "$repo" -c user.name=t -c user.email=t@t commit -qm base
  git -C "$repo" checkout -qb work
  printf '%s' "$repo"
}

commit() {
  git -C "$1" add -A
  git -C "$1" -c user.name=t -c user.email=t@t commit -qm change
}

run() {
  : > "$work/log"
  (cd "$1" && PATH="$work/bin:$PATH" PNPM_LOG="$work/log" PNPM_STATUS="${2:-0}" "$script" --base base 2>&1)
}

echo 'ui-pre-push.sh'

repo=$(fixture backend)
echo 'echo bye' > "$repo/scripts/x.sh"; commit "$repo"
out=$(run "$repo"); status=$?
if [ "$status" -eq 0 ] && [[ "$out" == *'skipping the UI tests'* ]] && [ ! -s "$work/log" ]; then
  pass 'a push with no UI change skips without calling pnpm'
else
  fail 'a push with no UI change skips without calling pnpm' 'exit 0, skip message, no pnpm call' "exit $status: $out / $(cat "$work/log")"
fi

repo=$(fixture ui)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/a.ts"; commit "$repo"
out=$(run "$repo"); status=$?
want=$'pnpm --dir src/Anichron.UI install --frozen-lockfile\npnpm --dir src/Anichron.UI test'
if [ "$status" -eq 0 ] && [ "$(cat "$work/log")" = "$want" ]; then
  pass 'a UI change installs the frozen lockfile, then runs the tests'
else
  fail 'a UI change installs the frozen lockfile, then runs the tests' "$want" "exit $status: $(cat "$work/log")"
fi

repo=$(fixture failing)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/a.ts"; commit "$repo"
out=$(run "$repo" 1); status=$?
if [ "$status" -eq 1 ]; then pass 'failing UI tests fail the push'; else fail 'failing UI tests fail the push' 'exit 1' "exit $status: $out"; fi

repo=$(fixture uncommitted)
echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/a.ts"
out=$(run "$repo"); status=$?
if [ "$status" -eq 0 ] && [ ! -s "$work/log" ]; then
  pass 'an uncommitted UI change is not pushed, so it is skipped'
else
  fail 'an uncommitted UI change is not pushed, so it is skipped' 'exit 0, no pnpm call' "exit $status: $(cat "$work/log")"
fi

repo=$(fixture nobase)
out=$(cd "$repo" && "$script" --base missing 2>&1); status=$?
if [ "$status" -eq 2 ]; then pass 'an unknown base exits 2'; else fail 'an unknown base exits 2' 'exit 2' "exit $status: $out"; fi

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi
echo 'all passed'
