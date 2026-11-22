#!/usr/bin/env bash
# Tests for ui-mutation-promote.sh. Run: scripts/ui-mutation-promote.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/ui-mutation-promote.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

work=$(mktemp -d "${TMPDIR:-/tmp}/ui-mutation-promote-test.XXXXXXXX") \
  || { echo 'could not create a temp dir' >&2; exit 1; }
trap 'rm -rf "$work"' EXIT

git_as_mira() { git -c user.name=mira -c user.email=mira@example.org "$@"; }

# A master with two commits: the first is where master's baseline was measured,
# the second (HEAD) changes README.md or, with `ui`, a UI file.
fixture() {
  local repo="$work/$1"
  mkdir -p "$repo/src/Anichron.UI/src" "$work/state/$1"
  echo 'export const a = 1;' > "$repo/src/Anichron.UI/src/a.ts"
  echo 'readme' > "$repo/README.md"
  git -C "$repo" init -q -b master
  git -C "$repo" add -A
  git_as_mira -C "$repo" commit -qm first
  if [ "$2" = ui ]; then echo 'export const a = 2;' > "$repo/src/Anichron.UI/src/a.ts"; else echo 'more' >> "$repo/README.md"; fi
  git -C "$repo" add -A
  git_as_mira -C "$repo" commit -qm second
  printf '%s' "$repo"
}

state() { printf '%s' "$work/state/$(basename "$1")"; }
first_of() { git -C "$1" rev-parse HEAD^; }

# The pull request's artifact for HEAD's tree: absent, expired or present.
pr_artifact() {
  local list
  list="$(state "$1")/ui-mutation-pr-$(git -C "$1" rev-parse 'HEAD^{tree}').list"
  case "$2" in
    none) ;;
    expired) echo '{"artifacts":[{"id":5,"expired":true}]}' > "$list" ;;
    *) echo '{"artifacts":[{"id":5,"expired":false}]}' > "$list"
       echo "$2" > "$(state "$1")/5.json" ;;
  esac
}

master_artifact() {
  printf '{"artifacts":[{"id":7,"expired":false,"workflow_run":{"head_branch":"master","head_sha":"%s"}}]}\n' \
    "$(first_of "$1")" > "$(state "$1")/ui-mutation-incremental.list"
  echo '{"from":"master"}' > "$(state "$1")/7.json"
}

check() {
  local label="$1" repo="$2" want="$3" want_file="$4" out status actual
  out=$(cd "$repo" && PATH="$here/fixtures/fake-gh:$PATH" GH_STATE="$(state "$repo")" "$script" 2>/dev/null); status=$?
  actual=$(cat "$repo/src/Anichron.UI/reports/stryker-incremental.json" 2>/dev/null || echo '<missing>')
  if [ "$status" -ne 0 ] || [ "$out" != "$want" ] || [ "$actual" != "$want_file" ]; then
    fail "$label" "exit 0, '$want', $want_file" "exit $status, '$out', $actual"
  else
    pass "$label"
  fi
}

echo 'ui-mutation-promote.sh'

repo=$(fixture promoted ui)
pr_artifact "$repo" '{"from":"pr"}'
master_artifact "$repo"
check "the pull request's artifact for the tree is promoted" "$repo" 'baseline=promoted' '{"from":"pr"}'

repo=$(fixture expired ui)
pr_artifact "$repo" expired
master_artifact "$repo"
check "an expired pull request artifact falls back to master's" "$repo" \
  $'commit='"$(first_of "$repo")"$'\nbaseline=stale' '{"from":"master"}'

repo=$(fixture carried readme)
pr_artifact "$repo" none
master_artifact "$repo"
check "master's baseline is carried when the UI files are the same" "$repo" 'baseline=carried' '{"from":"master"}'

repo=$(fixture stale ui)
pr_artifact "$repo" none
master_artifact "$repo"
check "master's baseline behind the UI files is stale, with its commit" "$repo" \
  $'commit='"$(first_of "$repo")"$'\nbaseline=stale' '{"from":"master"}'
recorded=$(cat "$repo/src/Anichron.UI/reports/stryker-incremental.commit" 2>/dev/null)
if [ "$recorded" = "$(first_of "$repo")" ]; then pass 'a stale baseline records its commit for the run'; else fail 'a stale baseline records its commit for the run' "$(first_of "$repo")" "$recorded"; fi

repo=$(fixture missing ui)
pr_artifact "$repo" none
check 'no baseline anywhere is missing' "$repo" 'baseline=missing' '<missing>'

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi
echo 'all passed'
