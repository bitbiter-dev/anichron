#!/usr/bin/env bash
# Tests for assemble-pages.sh. Run: scripts/assemble-pages.test.sh
#
# EVERY dangerous path in this file is a FAKE built inside a throwaway temp
# directory. Nothing real is ever passed to the script under test.
#
# That rule is not decoration. The predecessor of this file passed the real
# $HOME to prove the script refused it, ran before the guard it was testing
# existed, and deleted a home directory on 2026-09-21. The guard now bounds
# itself with a sentinel file, and it was hand-verified before this harness was
# written -- but the test still uses fakes, because a test that needs the guard
# to work in order to be safe is a test that can only be run once.
#
# So: to assert "$HOME is refused", construct a fake home and point HOME at it
# in a subshell. Never `~`, never the real path.
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/assemble-pages.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

assert_eq() {
  if [ "$2" = "$3" ]; then pass "$1"; else fail "$1" "$2" "$3"; fi
}

assert_file() {
  if [ -f "$2" ]; then pass "$1"; else fail "$1" "file $2 to exist" 'it does not'; fi
}

assert_no_path() {
  if [ ! -e "$2" ]; then pass "$1"; else fail "$1" "$2 not to exist" 'it does'; fi
}

assert_contains_file() {
  if grep -q "$2" "$3" 2>/dev/null; then pass "$1"; else fail "$1" "'$2' in $3" "$(head -c 200 "$3" 2>/dev/null)"; fi
}

# Runs the script expecting FAILURE, and checks the message. `$1` label,
# `$2` expected substring on stderr, rest is the command.
assert_fails() {
  local label="$1" want="$2"; shift 2
  local out rc
  out=$("$@" 2>&1); rc=$?
  if [ "$rc" -eq 0 ]; then
    fail "$label" "non-zero exit mentioning '$want'" "exit 0: $out"
  else
    case "$out" in
      *"$want"*) pass "$label" ;;
      *) fail "$label" "error mentioning '$want'" "$out" ;;
    esac
  fi
}

work=$(mktemp -d "${TMPDIR:-/tmp}/assemble-pages-test.XXXXXXXX") \
  || { echo 'could not create a temp dir' >&2; exit 1; }
trap 'rm -rf "$work"' EXIT

# Fixtures: the smallest shape each report generator actually produces. Coverage
# is identified by index.html, mutation by mutation-report.html.
cov="$work/coverage-report"
mut="$work/mutation-report"
mkdir -p "$cov/_subdir" "$mut"
echo '<html>coverage</html>' > "$cov/index.html"
echo 'badge' > "$cov/badge_linecoverage.svg"
echo 'nested' > "$cov/_subdir/nested.html"
echo '.dotfile' > "$cov/.hidden"
echo '<html>mutation</html>' > "$mut/mutation-report.html"
echo '{}' > "$mut/mutation-report.json"

echo "assembling"

site="$work/site"
out=$("$script" "$cov" "$mut" "$site" 2>&1)
assert_eq 'reports what it published' 'assembled' "$(echo "$out" | awk '{print $1}')"
assert_file 'writes a landing page at the root' "$site/index.html"
assert_file 'puts the coverage report under coverage/' "$site/coverage/index.html"
assert_file 'puts the mutation report under mutation/' "$site/mutation/mutation-report.html"
assert_file 'writes .nojekyll so underscore paths are served' "$site/.nojekyll"
assert_file 'carries nested directories' "$site/coverage/_subdir/nested.html"
assert_file 'carries dotfiles' "$site/coverage/.hidden"
assert_contains_file 'landing page links the coverage report' 'coverage/index.html' "$site/index.html"
assert_contains_file 'landing page links the mutation report' 'mutation/mutation-report.html' "$site/index.html"

# The coverage badge the publish step reads must survive the move, since the
# badge step takes it from the original directory, not from the site.
assert_file 'leaves the source coverage report untouched' "$cov/badge_linecoverage.svg"

echo
echo "a missing mutation report is not an error"

# Stryker runs under continue-on-error, so a crash yields no HTML report.
# Publishing coverage alone beats failing a required check twice and shipping
# nothing -- see the script header.
site_nomut="$work/site-no-mutation"
out=$("$script" "$cov" "$work/does-not-exist" "$site_nomut" 2>&1)
assert_eq 'exits 0 with no mutation report' '0' "$?"
assert_file 'still publishes coverage' "$site_nomut/coverage/index.html"
assert_no_path 'creates no empty mutation directory' "$site_nomut/mutation"
assert_contains_file 'landing page says the report is unavailable' 'Not available for this build' "$site_nomut/index.html"
assert_contains_file 'landing page explains it failed rather than scored badly' 'failed rather than scored badly' "$site_nomut/index.html"

# A directory that exists but holds no HTML report is the same case as an absent
# one: Stryker creates its output directory before it knows whether it will finish.
mkdir -p "$work/empty-mutation"
site_emptymut="$work/site-empty-mutation"
"$script" "$cov" "$work/empty-mutation" "$site_emptymut" >/dev/null 2>&1
assert_no_path 'an empty mutation directory publishes nothing' "$site_emptymut/mutation"

echo
echo "refusing to delete the wrong thing"

# THE case that matters. The output directory is only removed when it carries
# the sentinel this script writes, so a directory it did not create is refused
# rather than deleted.
foreign="$work/someone-elses-directory"
mkdir -p "$foreign"
echo 'precious' > "$foreign/important.txt"
assert_fails 'refuses a directory it did not create' 'did not create it' \
  "$script" "$cov" "$mut" "$foreign"
assert_file 'the foreign file survives the refusal' "$foreign/important.txt"

# Re-running over its own output is the normal case and must still work.
"$script" "$cov" "$mut" "$site" >/dev/null 2>&1
assert_eq 'rebuilds its own output directory' '0' "$?"
assert_file 'the rebuild is complete' "$site/coverage/index.html"

# A fake home, never the real one.
fake_home="$work/fake-home"
mkdir -p "$fake_home"
echo 'precious' > "$fake_home/.zshrc"
assert_fails 'refuses $HOME' 'refusing to write the site to' \
  env HOME="$fake_home" "$script" "$cov" "$mut" "$fake_home"
assert_file 'the fake dotfile survives' "$fake_home/.zshrc"

# The ordinary-use footgun: an output directory that contains an input. The
# earlier version passed its input checks and then deleted the coverage report
# before cp could read it.
assert_fails 'refuses an output that contains an input' 'contains the input' \
  "$script" "$cov" "$mut" "$work"
assert_file 'the input survives that refusal' "$cov/index.html"

assert_fails 'refuses an output that is also an input' 'also an input' \
  "$script" "$cov" "$mut" "$cov"
assert_file 'the input survives being named as the output' "$cov/index.html"

# `.` and `..` are refused by name rather than relying on rm's self-protection,
# which is what saved three of the four cases in the original incident.
assert_fails 'refuses . as the output' 'relative directory reference' \
  "$script" "$cov" "$mut" "$work/."
assert_fails 'refuses .. as the output' 'relative directory reference' \
  "$script" "$cov" "$mut" "$work/.."

assert_fails 'refuses the filesystem root' 'refusing to write the site to' \
  "$script" "$cov" "$mut" "/"

# A path whose parent does not exist cannot be resolved, so it is refused
# rather than guessed at.
assert_fails 'refuses an output whose parent is missing' 'parent directory does not exist' \
  "$script" "$cov" "$mut" "$work/no/such/parent/site"

echo
echo "input validation"

assert_fails 'refuses a missing coverage directory' 'coverage report directory not found' \
  "$script" "$work/does-not-exist" "$mut" "$work/site-x"
assert_no_path 'leaves no partial output when coverage is missing' "$work/site-x"

mkdir -p "$work/coverage-no-index"
assert_fails 'refuses a coverage directory with no index.html' 'no index.html' \
  "$script" "$work/coverage-no-index" "$mut" "$work/site-y"
assert_no_path 'leaves no partial output when coverage has no index' "$work/site-y"

assert_fails 'no arguments prints usage' 'usage:' "$script"
assert_fails 'one argument prints usage' 'usage:' "$script" "$cov"
assert_fails 'two arguments prints usage' 'usage:' "$script" "$cov" "$mut"

echo
if [ "$failures" -gt 0 ]; then
  printf '%d failure(s)\n' "$failures"
  exit 1
fi
echo 'all tests passed'
