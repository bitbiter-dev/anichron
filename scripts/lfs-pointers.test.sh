#!/usr/bin/env bash
# Tests for lfs-pointers.sh against throwaway local repositories.
# Run: scripts/lfs-pointers.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/lfs-pointers.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

assert_contains() {
  case "$3" in
    *"$2"*) pass "$1" ;;
    *) fail "$1" "output containing '$2'" "$3" ;;
  esac
}

assert_passes() {
  local label="$1" out status
  out=$("$script" 2>&1); status=$?
  if [ "$status" -eq 0 ]; then pass "$label"; else fail "$label" 'exit 0' "exit $status with: $out"; fi
}

assert_fails() {
  local label="$1" needle="$2" out status
  out=$("$script" 2>&1); status=$?
  if [ "$status" -eq 1 ]; then
    assert_contains "$label" "$needle" "$out"
  else
    fail "$label" 'exit 1' "exit $status with: $out"
  fi
}

work=$(mktemp -d "${TMPDIR:-/tmp}/lfs-pointers-test.XXXXXXXX") \
  || { echo 'could not create a temp dir' >&2; exit 1; }
[ -n "$work" ] && [ -d "$work" ] || { echo 'temp dir is not usable' >&2; exit 1; }
trap 'rm -rf "$work"' EXIT

repo=0
new_repo() {
  repo=$((repo + 1))
  git init --quiet "$work/$repo" || { echo 'could not create a throwaway repo' >&2; exit 1; }
  cd "$work/$repo" || { echo 'could not enter the throwaway repo' >&2; exit 1; }
  git config filter.lfs.clean 'git-lfs clean -- %f'
  git config filter.lfs.smudge 'git-lfs smudge -- %f'
  git config filter.lfs.process 'git-lfs filter-process'
  git config filter.lfs.required true
  printf '*.jpg filter=lfs diff=lfs merge=lfs -text\n' > .gitattributes
  printf 'hello\n' > readme.md
  git add .gitattributes readme.md
}

binary() { printf 'JFIF\0\1\2\3' > "$1"; }

echo "text and pointers"

new_repo
assert_passes 'only text files'

new_repo
binary photo.jpg
git add photo.jpg
assert_passes 'a binary stored through LFS'

new_repo
: > empty.dat
git add empty.dat
assert_passes 'an empty file'

echo "binaries outside LFS"

new_repo
binary blob.bin
git add blob.bin
assert_fails 'a staged binary outside .gitattributes' 'blob.bin'

new_repo
binary blob.bin
git add blob.bin
git -c user.name=t -c user.email=t@example.com commit --quiet -m binary
assert_fails 'a committed binary outside .gitattributes' 'blob.bin'

new_repo
binary photo.jpg
git -c filter.lfs.process= -c filter.lfs.clean=cat -c filter.lfs.required=false add photo.jpg
assert_fails 'a binary matching .gitattributes but staged without LFS' 'photo.jpg'

new_repo
binary 'my photo.bin'
git add 'my photo.bin'
assert_fails 'a path with a space is named whole' '  my photo.bin'

new_repo
binary one.bin
binary two.bin
git add one.bin two.bin
out=$("$script" 2>&1)
assert_contains 'every binary is named, not only the first (one)' 'one.bin' "$out"
assert_contains 'every binary is named, not only the first (two)' 'two.bin' "$out"

new_repo
binary blob.bin
git add blob.bin
mkdir sub
cd sub || { echo 'could not enter the subdirectory' >&2; exit 1; }
assert_fails 'runs from a subdirectory' 'blob.bin'

echo
if [ "$failures" -eq 0 ]; then
  echo 'all lfs-pointers tests passed'
else
  echo "$failures lfs-pointers test(s) failed"
  exit 1
fi
