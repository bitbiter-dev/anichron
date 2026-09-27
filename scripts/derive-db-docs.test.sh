#!/usr/bin/env bash
# Tests for derive-db-docs.sh. Run: scripts/derive-db-docs.test.sh
#
# These assert the SCRIPT's contract — regenerate, detect staleness, repair. They deliberately
# do not assert the DDL's content: that is `dotnet ef`'s output, and re-asserting it here would
# be a second description of the schema to keep in step, which is the duplication this whole
# change exists to remove.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/.." && pwd)"
script="$here/derive-db-docs.sh"
doc="$root/docs/schema.sql"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

assert_eq() {
  if [ "$2" = "$3" ]; then pass "$1"; else fail "$1" "$2" "$3"; fi
}

assert_contains() {
  case "$3" in
    *"$2"*) pass "$1" ;;
    *) fail "$1" "output containing '$2'" "$3" ;;
  esac
}

echo "regenerate"

assert_eq "the script is executable" 'yes' "$([ -x "$script" ] && echo yes || echo no)"

before=$(cat "$doc" 2>/dev/null || echo '<missing>')
out=$("$script" 2>&1); status=$?
assert_eq "regenerating succeeds" '0' "$status"
assert_contains "it says what it did" 'regenerated from the EF Core model' "$out"
assert_eq "and the committed file already matched the model" "$before" "$(cat "$doc")"

echo "--check"

out=$("$script" --check 2>&1); status=$?
assert_eq "--check passes against a current file" '0' "$status"
assert_contains "and says so" 'is current' "$out"

# ⭐ The gate is only worth having if it FAILS. Asserted by making the file stale on purpose,
# because a check that has never been seen to fail is indistinguishable from one that cannot.
printf '\n-- deliberate drift\n' >> "$doc"
out=$("$script" --check 2>&1); status=$?
assert_eq "--check fails against a stale file" '1' "$status"
assert_contains "names the file" 'docs/schema.sql is stale' "$out"
assert_contains "shows the drift rather than only announcing it" 'deliberate drift' "$out"
assert_contains "says how to fix it" 'scripts/derive-db-docs.sh' "$out"

echo "repair"

out=$("$script" 2>&1)
assert_eq "regenerating restores the file exactly" "$before" "$(cat "$doc")"
assert_contains "and reports that it changed" 'CHANGED' "$out"

printf '\n'
if [ "$failures" -eq 0 ]; then
  printf 'all derive-db-docs tests passed\n'
else
  printf '%s derive-db-docs test(s) failed\n' "$failures"
fi
exit $((failures > 0))
