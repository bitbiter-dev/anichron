#!/usr/bin/env bash
# Tests for tool-versions.sh. Run: scripts/tool-versions.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/tool-versions.sh"
failures=0
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

assert_contains() {
  case "$3" in
    *"$2"*) pass "$1" ;;
    *) fail "$1" "output containing '$2'" "$3" ;;
  esac
}

assert_exits() {
  local label="$1" expected="$2" needle="$3"; shift 3
  local out status
  out=$("$@" 2>&1); status=$?
  if [ "$status" -ne "$expected" ]; then
    fail "$label" "exit $expected" "exit $status with: $out"
  else
    assert_contains "$label" "$needle" "$out"
  fi
}

manifest() {
  printf '{"version":1,"isRoot":true,"tools":{"husky":{"version":"0.9.1","commands":["husky"]}%s}}\n' \
    "${1:+,\"dotnet-ef\":{\"version\":\"$1\",\"commands\":[\"dotnet-ef\"]\}}" > "$work/tools.json"
}

props() {
  {
    echo '<Project>'
    echo '  <ItemGroup>'
    [ -n "$1" ] && echo "    <PackageVersion Include=\"Microsoft.EntityFrameworkCore\" Version=\"$1\" />"
    echo '    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="9.9.9" />'
    echo '  </ItemGroup>'
    echo '</Project>'
  } > "$work/Directory.Packages.props"
}

run() { "$script" "$work/tools.json" "$work/Directory.Packages.props"; }

echo "tool-versions"

manifest 10.0.13; props 10.0.13
assert_exits 'equal versions pass' 0 '' run

manifest 10.0.13; props 10.0.14
assert_exits 'a package ahead of the tool fails and names both' 1 'dotnet-ef 10.0.13 does not match Microsoft.EntityFrameworkCore 10.0.14' run

manifest 10.0.14; props 10.0.13
assert_exits 'a tool ahead of the package fails' 1 'Set dotnet-ef in .config/dotnet-tools.json to 10.0.13' run

manifest 10.0.13; props 10.0.13
sed -i.bak 's/"Microsoft.EntityFrameworkCore"/"Microsoft.EntityFrameworkCore.Design"/' "$work/Directory.Packages.props"
assert_exits 'a similarly named package is not the one compared' 2 'Microsoft.EntityFrameworkCore is not in' run

manifest ''; props 10.0.13
assert_exits 'a manifest without dotnet-ef is refused' 2 'dotnet-ef is not in' run

assert_exits 'a missing manifest is refused' 2 'does not exist' \
  "$script" "$work/absent.json" "$work/Directory.Packages.props"

echo
if [ "$failures" -eq 0 ]; then
  echo 'all tests passed'
else
  echo "$failures tool-versions test(s) failed"
  exit 1
fi
