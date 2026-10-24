#!/usr/bin/env bash
# Tests for mutation-targets.sh. Run: scripts/mutation-targets.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/mutation-targets.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/mutation-targets-test.XXXXXXXX") \
  || { echo 'could not create a temp dir' >&2; exit 1; }
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }
assert_eq() { if [ "$2" = "$3" ]; then pass "$1"; else fail "$1" "$2" "$3"; fi; }

src="$work/src"
mkdir -p "$src"
project() {
  mkdir -p "$src/$1"
  printf '<Project>\n  <PropertyGroup>%s</PropertyGroup>\n</Project>\n' "$2" > "$src/$1/$1.csproj"
}
project Zeta '<IsMutationTarget>true</IsMutationTarget>'
project Alpha '<IsMutationTarget>True</IsMutationTarget>'
project Alpha.Tests.Unit '<IsTestCodeProject>True</IsTestCodeProject>'
project Helpers '<IsMutationTarget>false</IsMutationTarget>'
project Unset ''
cat > "$src/Anichron.slnx" <<'XML'
<Solution>
  <Folder Name="/Solution Items/">
    <File Path="docker-compose.yml" />
  </Folder>
  <Project Path="Zeta/Zeta.csproj" />
  <Project Path="Alpha/Alpha.csproj" />
  <Project Path="Alpha.Tests.Unit/Alpha.Tests.Unit.csproj" />
  <Project Path="Helpers/Helpers.csproj" />
  <Project Path="Unset/Unset.csproj" />
</Solution>
XML

echo 'mutation-targets.sh'

out=$("$script" "$src" 2>&1); status=$?
assert_eq 'lists the projects that evaluate IsMutationTarget to true, sorted' \
  "$(printf 'Alpha/Alpha.csproj\nZeta/Zeta.csproj')" "$out"
assert_eq 'exits 0' 0 "$status"

out=$("$script" --opted-out "$src" 2>&1)
assert_eq '--opted-out lists the non-test projects that are not targets' \
  "$(printf 'Helpers/Helpers.csproj\nUnset/Unset.csproj')" "$out"

printf '<Project>\n' > "$src/Helpers/Helpers.csproj"
out=$("$script" "$src" 2>&1); status=$?
assert_eq 'a project that cannot be evaluated exits 2' 2 "$status"
case "$out" in *'Helpers/Helpers.csproj'*) pass 'and is named' ;; *) fail 'and is named' 'Helpers/Helpers.csproj' "$out" ;; esac

out=$("$script" "$work/nowhere" 2>&1); status=$?
assert_eq 'a missing solution exits 2' 2 "$status"

echo
if [ "$failures" -eq 0 ]; then
  echo 'all mutation-targets tests passed'
else
  echo "$failures mutation-targets test(s) failed"
  exit 1
fi
