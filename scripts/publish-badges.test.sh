#!/usr/bin/env bash
# Integration tests for publish-badges.sh against a throwaway local repository.
# Run: scripts/publish-badges.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/publish-badges.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }
assert_eq() { if [ "$2" = "$3" ]; then pass "$1"; else fail "$1" "$2" "$3"; fi; }

assert_absent_on_badges() {
  if git show "origin/badges:$2" >/dev/null 2>&1; then
    fail "$1" "$2 absent from the badges branch" "$2 is present"
  else
    pass "$1"
  fi
}

# This script runs destructive git commands — pushes, force-pushes and a branch
# deletion — and in CI it runs inside a checkout that holds push credentials and
# `contents: write`. Every step of the setup is therefore guarded: without -e a
# failed `cd` would silently leave the rest of the script operating on the real
# repository and its real origin.
work=$(mktemp -d) || { echo 'could not create a temp dir' >&2; exit 1; }
[ -n "$work" ] && [ -d "$work" ] || { echo 'temp dir is not usable' >&2; exit 1; }
trap 'rm -rf "$work"' EXIT

# A bare repo stands in for origin, so the push path is exercised for real.
git init --quiet --bare "$work/origin.git" || { echo 'could not create the throwaway origin' >&2; exit 1; }
git clone --quiet "$work/origin.git" "$work/repo" 2>/dev/null || { echo 'could not clone the throwaway origin' >&2; exit 1; }
cd "$work/repo" || { echo 'could not enter the throwaway clone' >&2; exit 1; }

# Second layer, deliberately redundant with the guards above: refuse to run at
# all unless origin really is the throwaway. A bug in the setup must not be able
# to point the destructive commands below at a real remote.
origin_url=$(git remote get-url origin 2>/dev/null) || { echo 'throwaway clone has no origin' >&2; exit 1; }
case "$origin_url" in
  "$work"/*) : ;;
  *) printf 'refusing to run: origin is %s, not the throwaway repo under %s\n' "$origin_url" "$work" >&2; exit 1 ;;
esac
git config user.name 'Test'
git config user.email 'test@example.com'
git config commit.gpgsign false
echo 'source' > app.txt
git add app.txt
git commit --quiet -m 'initial'
git branch -M master
git push --quiet -u origin master

# An existing badges branch carrying a file this run knows nothing about —
# stands in for the ratchet's high-water mark (#183).
git checkout --quiet --orphan badges
git rm -rf . --quiet
echo 'old-coverage' > coverage.svg
echo '{"highWaterMark":46.58}' > ratchet.json
git add coverage.svg ratchet.json
git commit --quiet -m 'existing badges'
git push --quiet -u origin badges
git checkout --quiet master

mkdir -p coverage/report
echo 'new-coverage' > coverage/report/badge_linecoverage.svg
echo '{"schemaVersion":1,"label":"mutation","message":"46.58%","color":"yellow"}' > "$work/mutation.json"

echo "publish-badges"

MUTATION_BADGE="$work/mutation.json" "$script" coverage/report/badge_linecoverage.svg > /dev/null 2>&1
rc=$?
assert_eq "publishes successfully" '0' "$rc"

git fetch --quiet origin badges
assert_eq "updates the coverage badge" 'new-coverage' "$(git show origin/badges:coverage.svg 2>&1)"
assert_eq "adds the mutation badge" 'yellow' "$(git show origin/badges:mutation.json 2>&1 | jq -r '.color')"
assert_eq "preserves an unrelated artifact it knows nothing about" '{"highWaterMark":46.58}' "$(git show origin/badges:ratchet.json 2>&1)"
assert_absent_on_badges "leaves the source tree off the badges branch" app.txt
assert_eq "returns the working tree to the original branch" 'master' "$(git rev-parse --abbrev-ref HEAD)"

# Second run with no mutation payload must not delete the badge it published.
unset MUTATION_BADGE
echo 'newer-coverage' > coverage/report/badge_linecoverage.svg
"$script" coverage/report/badge_linecoverage.svg > /dev/null 2>&1
git fetch --quiet origin badges
assert_eq "a run without a payload keeps the previous mutation badge" 'yellow' "$(git show origin/badges:mutation.json 2>&1 | jq -r '.color')"
assert_eq "and still updates coverage" 'newer-coverage' "$(git show origin/badges:coverage.svg 2>&1)"

# The shape CI actually produces on a refused report: redirection creates the
# file before the generator runs, so the path exists and is zero bytes. An
# earlier version copied it, force-pushed, and destroyed a working badge.
: > "$work/empty.json"
MUTATION_BADGE="$work/empty.json" "$script" coverage/report/badge_linecoverage.svg > /dev/null 2>&1
git fetch --quiet origin badges
assert_eq "an empty payload does not overwrite a good badge" 'yellow' "$(git show origin/badges:mutation.json 2>&1 | jq -r '.color')"

printf '{"schemaVersion":1,"label":"muta' > "$work/truncated.json"
MUTATION_BADGE="$work/truncated.json" "$script" coverage/report/badge_linecoverage.svg > /dev/null 2>&1
git fetch --quiet origin badges
assert_eq "a truncated payload does not overwrite a good badge" 'yellow' "$(git show origin/badges:mutation.json 2>&1 | jq -r '.color')"

printf '{"schemaVersion":1,"label":"mutation"}' > "$work/no-message.json"
MUTATION_BADGE="$work/no-message.json" "$script" coverage/report/badge_linecoverage.svg > /dev/null 2>&1
git fetch --quiet origin badges
assert_eq "a payload without a message does not overwrite a good badge" 'yellow' "$(git show origin/badges:mutation.json 2>&1 | jq -r '.color')"

# Creating the branch from scratch must not carry the source tree over.
git push --quiet origin --delete badges
git branch --quiet -D badges
"$script" coverage/report/badge_linecoverage.svg > /dev/null 2>&1
git fetch --quiet origin badges
assert_eq "creates the branch when absent" 'newer-coverage' "$(git show origin/badges:coverage.svg 2>&1)"
assert_absent_on_badges "a freshly created branch carries no source" app.txt

# Genuine divergence, which is the dangerous case: the local `badges` holds a
# commit the remote does not, because the remote was rebuilt on an unrelated
# history. A non-forcing refspec is *rejected* here rather than fast-forwarding,
# so continuing from the stale local ref would force-push over the remote and
# destroy everything on it. A remote that is merely ahead fast-forwards fine and
# would not exercise this.
git clone --quiet "$work/origin.git" "$work/other" 2>/dev/null || { echo 'could not clone for the divergence case' >&2; exit 1; }
(
  cd "$work/other" || exit 1
  git config user.name 'Other'
  git config user.email 'other@example.com'
  git config commit.gpgsign false
  git checkout --quiet --orphan rebuilt
  git rm -rf . --quiet 2>/dev/null || true
  echo '{"added":"by someone else"}' > other.json
  echo 'their-coverage' > coverage.svg
  git add other.json coverage.svg
  git commit --quiet -m 'another actor rebuilds the branch'
  git push --quiet --force origin rebuilt:badges
)
assert_eq "local badges really has diverged from the remote" 'diverged' "$(git rev-parse --verify --quiet badges >/dev/null && git fetch --quiet origin badges:badges 2>/dev/null && echo 'fast-forwarded' || echo 'diverged')"
MUTATION_BADGE="$work/mutation.json" "$script" coverage/report/badge_linecoverage.svg > /dev/null 2>&1
git fetch --quiet --force origin badges:refs/remotes/origin/badges
assert_eq "a diverged local branch does not clobber the remote's artifacts" '{"added":"by someone else"}' "$(git show origin/badges:other.json 2>&1)"

# An unreachable origin must not be read as "the branch does not exist yet",
# which would recreate the branch from scratch and wipe every artifact.
git remote set-url origin "$work/does-not-exist.git"
if MUTATION_BADGE="$work/mutation.json" "$script" coverage/report/badge_linecoverage.svg > /dev/null 2>&1; then
  fail "an unreachable origin is refused, not treated as a missing branch" 'non-zero exit' 'exit 0'
else
  pass "an unreachable origin is refused, not treated as a missing branch"
fi
git remote set-url origin "$work/origin.git"
git fetch --quiet origin badges
assert_eq "and the unreachable run left the remote untouched" '{"added":"by someone else"}' "$(git show origin/badges:other.json 2>&1)"

# actions/checkout leaves a detached HEAD for some event types, where
# `rev-parse --abbrev-ref HEAD` yields the literal string "HEAD".
git checkout --quiet master
detached_sha=$(git rev-parse HEAD)
git checkout --quiet --detach "$detached_sha"
"$script" coverage/report/badge_linecoverage.svg > /dev/null 2>&1
assert_eq "restores a detached HEAD to the same commit" "$detached_sha" "$(git rev-parse HEAD)"
assert_eq "and does not strand the tree on the badges branch" 'HEAD' "$(git rev-parse --abbrev-ref HEAD)"

echo
if [ "$failures" -gt 0 ]; then
  printf '%d failure(s)\n' "$failures"
  exit 1
fi
echo 'all tests passed'
