#!/usr/bin/env bash
# Tests for derive-openapi.sh. Run: scripts/derive-openapi.test.sh
#
# These assert the script's contract (regenerate, detect staleness, repair), not the document's
# content; ApiDocumentTests covers which routes it describes.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/.." && pwd)"
script="$here/derive-openapi.sh"
doc="$root/docs/openapi/v1.json"
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
assert_contains "it says what it did" 'regenerated from the API surface' "$out"
assert_eq "and the committed file already matched the API" "$before" "$(cat "$doc")"

echo "--check"

out=$("$script" --check 2>&1); status=$?
assert_eq "--check passes against a current file" '0' "$status"
assert_contains "and says so" 'is current' "$out"
assert_eq "and leaves no comparison file behind" 'no' "$([ -e "$root/docs/openapi/.v1.json.derived" ] && echo yes || echo no)"

printf '{"deliberate": "drift"}\n' > "$doc"
out=$("$script" --check 2>&1); status=$?
assert_eq "--check fails against a stale file" '1' "$status"
assert_contains "names the file" 'docs/openapi/v1.json is stale' "$out"
assert_contains "shows the drift rather than only announcing it" 'deliberate' "$out"
assert_contains "says how to fix it" 'scripts/derive-openapi.sh' "$out"
assert_eq "and does not repair the file itself" '{"deliberate": "drift"}' "$(cat "$doc")"

echo "repair"

out=$("$script" 2>&1)
assert_eq "regenerating restores the file exactly" "$before" "$(cat "$doc")"
assert_contains "and reports nothing to commit, since it now matches HEAD" 'No change' "$out"

printf '\n'
if [ "$failures" -eq 0 ]; then
  printf 'all derive-openapi tests passed\n'
else
  printf '%s derive-openapi test(s) failed\n' "$failures"
fi
exit $((failures > 0))
