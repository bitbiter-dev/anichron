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

project() {
  local repo="$1" name="$2" properties="$3"
  mkdir -p "$repo/src/$name"
  printf '<Project>\n  <PropertyGroup>%s</PropertyGroup>\n</Project>\n' "$properties" \
    > "$repo/src/$name/$name.csproj"
  printf '  <Project Path="%s/%s.csproj" />\n' "$name" "$name" >> "$repo/src/projects"
}

# A fresh repository per case, committed on `base`: the mutation targets App
# and Other, App's test project, and Lib, which opts out.
fixture() {
  local repo="$work/$1"
  mkdir -p "$repo/src/App/Sub" "$repo/docs"
  project "$repo" App '<IsMutationTarget>true</IsMutationTarget>'
  project "$repo" Other '<IsMutationTarget>true</IsMutationTarget>'
  project "$repo" App.Tests.Unit '<IsTestCodeProject>True</IsTestCodeProject>'
  project "$repo" Lib '<IsMutationTarget>false</IsMutationTarget>'
  { echo '<Solution>'; cat "$repo/src/projects"; echo '</Solution>'; } > "$repo/src/Anichron.slnx"
  rm "$repo/src/projects"
  cat > "$repo/src/stryker-config.json" <<'JSON'
{ "stryker-config": { "mutate": ["!**/Migrations/**", "!**/App/Program.cs"] } }
JSON
  echo 'class Gadget {}' > "$repo/src/Other/Gadget.cs"
  echo 'class Kit {}' > "$repo/src/Lib/Kit.cs"
  echo 'class Foo {}' > "$repo/src/App/Foo.cs"
  echo 'class Bar {}' > "$repo/src/App/Sub/Bar.cs"
  echo 'class FooTests {}' > "$repo/src/App.Tests.Unit/FooTests.cs"
  echo 'class OrphanTests {}' > "$repo/src/App.Tests.Unit/OrphanTests.cs"
  echo 'class Helper {}' > "$repo/src/App.Tests.Unit/Helper.cs"
  echo 'class FooHttpTests {}' > "$repo/src/App.Tests.Unit/FooHttpTests.cs"
  echo 'class EfStoreTests {}' > "$repo/src/App.Tests.Unit/EfStoreTests.cs"
  printf 'interface IStore {}\nsealed class EfStore : IStore {}\n' > "$repo/src/App/Store.cs"
  mkdir -p "$repo/src/Rules.Tests.Unit"
  echo 'class RulesTests {}' > "$repo/src/Rules.Tests.Unit/RulesTests.cs"
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

repo=$(fixture test-prefix)
echo 'class FooHttpTests { int t; }' > "$repo/src/App.Tests.Unit/FooHttpTests.cs"
out=$(run "$repo"); status=$?
assert_has_line 'a test change mutates the source its name starts with' '**/App/Foo.cs' "$out"
assert_eq_status 'a prefix-mapped change exits 0 in print mode' 0 "$status"

repo=$(fixture test-type)
echo 'class EfStoreTests { int t; }' > "$repo/src/App.Tests.Unit/EfStoreTests.cs"
out=$(run "$repo"); status=$?
assert_has_line 'a test change mutates the file declaring the type it is named after' '**/App/Store.cs' "$out"
assert_eq_status 'a type-mapped change exits 0 in print mode' 0 "$status"

repo=$(fixture test-no-production)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
echo 'class RulesTests { int t; }' > "$repo/src/Rules.Tests.Unit/RulesTests.cs"
out=$(run "$repo"); status=$?
assert_contains 'tests of no production project are named' 'not mutated (tests of no production project): Rules.Tests.Unit/RulesTests.cs' "$out"
assert_eq_status 'tests of no production project do not fail the run' 0 "$status"

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

echo "projects"

repo=$(fixture one-project)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
out=$(run "$repo")
assert_has_line 'the run is limited to the changed file'"'"'s project' 'App.csproj' "$out"
assert_no_line 'an unchanged project gets no run' 'Other.csproj' "$out"

repo=$(fixture two-projects)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
echo 'class Gadget { int g; }' > "$repo/src/Other/Gadget.cs"
out=$(run "$repo")
app=$(sed -n '/^App.csproj$/,/^--project$/p' <<<"$out")
other=$(sed -n '/^Other.csproj$/,$p' <<<"$out")
assert_has_line "a project's run mutates its own file" '**/App/Foo.cs' "$app"
assert_no_line "a project's run leaves the other project's file alone" '**/Other/Gadget.cs' "$app"
assert_has_line 'the second project gets a run of its own' '**/Other/Gadget.cs' "$other"
assert_has_line 'every run carries the config exclusions' '!**/App/Program.cs' "$other"

repo=$(fixture outside)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
echo 'class Kit { int k; }' > "$repo/src/Lib/Kit.cs"
out=$(run "$repo"); status=$?
assert_contains 'a file in an opted-out project is named' 'not mutated (in no mutation target project): Lib/Kit.cs' "$out"
assert_no_line 'a file in an opted-out project is not mutated' '**/Lib/Kit.cs' "$out"
assert_eq_status 'a file outside the targets does not fail the run' 0 "$status"

repo=$(fixture only-outside)
echo 'class Kit { int k; }' > "$repo/src/Lib/Kit.cs"
out=$(run "$repo"); status=$?
assert_contains 'only files outside the targets mean nothing to mutate' 'nothing to mutate' "$out"
assert_eq_status 'only files outside the targets exit 0' 0 "$status"

echo "runs"

real_dotnet=$(command -v dotnet)
stub="$work/stub-bin"
mkdir -p "$stub"
cat > "$stub/dotnet" <<'STUB'
#!/usr/bin/env bash
[ "$1" = msbuild ] && exec "$REAL_DOTNET" "$@"
if [ "$1" = test ]; then
  echo test >> "$STUB_RECORD"
  exit "$TEST_STATUS"
fi
project=$(sed -n '/^--project$/{n;p;}' < <(printf '%s\n' "$@"))
printf '%s %s\n' "$project" "$TMPDIR" >> "$STUB_RECORD"
[ -d "$TMPDIR" ] && echo "$project" >> "$STUB_RECORD.dir"
case "$project" in App.csproj) exit "$APP_STATUS" ;; *) exit "$OTHER_STATUS" ;; esac
STUB
chmod +x "$stub/dotnet"
shared="$work/shared-tmp"
mkdir -p "$shared"

stryker_runs() {
  local repo="$1" record="$2"
  (cd "$repo/src" && PATH="$stub:$PATH" REAL_DOTNET="$real_dotnet" TMPDIR="$shared" \
    STUB_RECORD="$record" APP_STATUS="$3" OTHER_STATUS="$4" TEST_STATUS="${5:-0}" "$script" --base base >/dev/null 2>&1)
}

repo=$(fixture isolated)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
echo 'class Gadget { int g; }' > "$repo/src/Other/Gadget.cs"
record="$work/record-isolated"
stryker_runs "$repo" "$record" 2 0; status=$?
assert_eq_status 'the unit tests run first' test "$(head -1 "$record")"
dirs=$(awk '$1 != "test" {print $2}' "$record" 2>/dev/null)
if [ "$(wc -l <<<"$dirs")" -eq 2 ] && [ "$(sort -u <<<"$dirs" | wc -l)" -eq 2 ] && ! grep -qxF "$shared" <<<"$dirs"; then
  pass 'each run has a temp dir of its own'
else
  fail 'each run has a temp dir of its own' "two distinct TMPDIRs other than $shared" "${dirs:-nothing recorded}"
fi
assert_eq_status 'both temp dirs exist while their runs go' 2 "$(wc -l < "$record.dir" 2>/dev/null | tr -d ' ')"
left=$(while IFS= read -r dir; do [ -e "$dir" ] && echo "$dir"; done <<<"$dirs")
if [ -z "$left" ]; then pass 'the temp dirs are removed afterwards'; else fail 'the temp dirs are removed afterwards' 'none left' "$left"; fi
assert_has_line 'a failed run does not stop the next project' 'Other.csproj' "$(awk '{print $1}' "$record")"
assert_eq_status 'the worst exit status is passed through' 2 "$status"

repo=$(fixture worst-last)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
echo 'class Gadget { int g; }' > "$repo/src/Other/Gadget.cs"
stryker_runs "$repo" "$work/record-worst" 1 3; status=$?
assert_eq_status 'a later, worse status wins' 3 "$status"

repo=$(fixture tests-fail)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
record="$work/record-tests-fail"
stryker_runs "$repo" "$record" 0 0 1; status=$?
assert_eq_status 'failing unit tests exit 1' 1 "$status"
assert_eq_status 'failing unit tests start no Stryker run' test "$(cat "$record")"

repo=$(fixture all-pass)
echo 'class Foo { int x; }' > "$repo/src/App/Foo.cs"
echo 'class Gadget { int g; }' > "$repo/src/Other/Gadget.cs"
stryker_runs "$repo" "$work/record-pass" 0 0; status=$?
assert_eq_status 'all runs passing exits 0' 0 "$status"

echo
if [ "$failures" -eq 0 ]; then
  echo 'all mutation-changed tests passed'
else
  echo "$failures mutation-changed test(s) failed"
  exit 1
fi
