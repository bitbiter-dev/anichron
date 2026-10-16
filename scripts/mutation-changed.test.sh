#!/usr/bin/env bash
# Tests for mutation-changed.sh. Run: scripts/mutation-changed.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/mutation-changed.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

assert_has_line() {
  if grep -qxF -- "$2" <<<"$3"; then pass "$1"; else fail "$1" "a line '$2'" "$3"; fi
}

assert_no_line() {
  if grep -qxF -- "$2" <<<"$3"; then fail "$1" "no line '$2'" "$3"; else pass "$1"; fi
}

assert_contains() {
  case "$3" in
    *"$2"*) pass "$1" ;;
    *) fail "$1" "output containing '$2'" "$3" ;;
  esac
}

work=$(mktemp -d "${TMPDIR:-/tmp}/mutation-changed-test.XXXXXXXX") \
  || { echo 'could not create a temp dir' >&2; exit 1; }
trap 'rm -rf "$work"' EXIT

# A fresh repository per case: src/App with a test project, committed on `base`.
fixture() {
  local repo="$work/$1"
  mkdir -p "$repo/src/App/Sub" "$repo/src/App.Tests.Unit" "$repo/docs"
  cat > "$repo/src/stryker-config.json" <<'JSON'
{ "stryker-config": { "mutate": ["!**/Migrations/**", "!**/App/Program.cs"] } }
JSON
  echo 'class Foo {}' > "$repo/src/App/Foo.cs"
  echo 'class Bar {}' > "$repo/src/App/Sub/Bar.cs"
  echo 'class FooTests {}' > "$repo/src/App.Tests.Unit/FooTests.cs"
  echo 'class OrphanTests {}' > "$repo/src/App.Tests.Unit/OrphanTests.cs"
  echo 'class Helper {}' > "$repo/src/App.Tests.Unit/Helper.cs"
  echo 'notes' > "$repo/docs/notes.md"
  git -C "$repo" init -q -b base
  git -C "$repo" -c user.name=t -c user.email=t@example.invalid add -A
  git -C "$repo" -c user.name=t -c user.email=t@example.invalid commit -qm base
  git -C "$repo" checkout -q -b work
  printf '%s' "$repo"
}

commit_all() {
  git -C "$1" -c user.name=t -c user.email=t@example.invalid add -A
  git -C "$1" -c user.name=t -c user.email=t@example.invalid commit -qm change
}

run() {
  local repo="$1"; shift
  (cd "$repo/src" && "$script" --base base --print "$@" 2>&1)
}

echo "selection"

repo=$(fixture none)
out=$(run "$repo"); status=$?
assert_contains 'no changes says so' 'nothing changed' "$out"
assert_eq_status() { if [ "$2" = "$3" ]; then pass "$1"; else fail "$1" "exit $2" "exit $3"; fi; }
assert_eq_status 'no changes exits 0' 0 "$status"

repo=$(fixture committed)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
commit_all "$repo"
out=$(run "$repo")
assert_has_line 'a committed production change is mutated' '**/App/Foo.cs' "$out"
assert_no_line 'an unchanged file is not mutated' '**/App/Sub/Bar.cs' "$out"
assert_has_line 'the config exclusions are passed along' '!**/Migrations/**' "$out"
assert_has_line 'every config exclusion is passed along' '!**/App/Program.cs' "$out"

repo=$(fixture worktree-states)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
git -C "$repo" add src/App/Foo.cs
echo 'class Bar { int y; }' > "$repo/src/App/Sub/Bar.cs"
echo 'class Baz {}' > "$repo/src/App/Baz.cs"
out=$(run "$repo")
assert_has_line 'a staged change is mutated' '**/App/Foo.cs' "$out"
assert_has_line 'an unstaged change is mutated' '**/App/Sub/Bar.cs' "$out"
assert_has_line 'an untracked file is mutated' '**/App/Baz.cs' "$out"

repo=$(fixture test-mapped)
echo 'class FooTests { int t; }' > "$repo/src/App.Tests.Unit/FooTests.cs"
out=$(run "$repo"); status=$?
assert_has_line 'a test change mutates the source it is named after' '**/App/Foo.cs' "$out"
assert_eq_status 'a fully mapped change exits 0 in print mode' 0 "$status"

repo=$(fixture deleted)
git -C "$repo" rm -q src/App/Sub/Bar.cs
echo 'notes 2' > "$repo/docs/notes.md"
commit_all "$repo"
out=$(run "$repo")
assert_no_line 'a deleted file is not mutated' '**/App/Sub/Bar.cs' "$out"
assert_contains 'non-C# changes alone mean nothing to mutate' 'nothing changed' "$out"

echo "unmapped test changes"

repo=$(fixture unmapped)
echo 'class OrphanTests { int t; }' > "$repo/src/App.Tests.Unit/OrphanTests.cs"
echo 'class Helper { int h; }' > "$repo/src/App.Tests.Unit/Helper.cs"
out=$(run "$repo"); status=$?
assert_contains 'an unmapped test file is named' 'App.Tests.Unit/OrphanTests.cs' "$out"
assert_contains 'a changed test helper is named' 'App.Tests.Unit/Helper.cs' "$out"
assert_eq_status 'unmapped test changes fail rather than pass vacuously' 1 "$status"

out=$(run "$repo" App/Sub/Bar.cs); status=$?
assert_has_line 'explicit source files are mutated' '**/App/Sub/Bar.cs' "$out"
assert_eq_status 'explicit source files acknowledge the unmapped tests' 0 "$status"

out=$(run "$repo" App/Missing.cs); status=$?
assert_contains 'an explicit file that does not exist is refused' 'App/Missing.cs' "$out"
assert_eq_status 'a missing explicit file exits 2' 2 "$status"

echo "arguments"

repo=$(fixture bad-base)
out=$( (cd "$repo/src" && "$script" --base no-such-ref --print 2>&1) ); status=$?
assert_contains 'an unknown base is refused' 'no-such-ref' "$out"
assert_eq_status 'an unknown base exits 2' 2 "$status"

echo "isolation"

repo=$(fixture isolated)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
stub="$work/stub-bin"
mkdir -p "$stub"
cat > "$stub/dotnet" <<'STUB'
#!/usr/bin/env bash
printf '%s' "$TMPDIR" > "$STUB_RECORD"
[ -d "$TMPDIR" ] && echo present >> "$STUB_RECORD.dir"
exit 2
STUB
chmod +x "$stub/dotnet"
record="$work/stub-record"
shared="$work/shared-tmp"
mkdir -p "$shared"
(cd "$repo/src" && PATH="$stub:$PATH" TMPDIR="$shared" STUB_RECORD="$record" "$script" --base base >/dev/null 2>&1)
status=$?
used=$(cat "$record" 2>/dev/null)
if [ -n "$used" ] && [ "$used" != "$shared" ]; then
  pass 'stryker runs with a temp dir of its own'
else
  fail 'stryker runs with a temp dir of its own' "a TMPDIR other than $shared" "${used:-nothing recorded}"
fi
assert_contains 'the private temp dir exists while stryker runs' 'present' "$(cat "$record.dir" 2>/dev/null)"
if [ -n "$used" ] && [ ! -e "$used" ]; then
  pass 'the private temp dir is removed afterwards'
else
  fail 'the private temp dir is removed afterwards' 'no directory left behind' "${used:-nothing recorded}"
fi
assert_eq_status "stryker's exit status is passed through" 2 "$status"

echo
if [ "$failures" -eq 0 ]; then
  echo 'all mutation-changed tests passed'
else
  echo "$failures mutation-changed test(s) failed"
  exit 1
fi
