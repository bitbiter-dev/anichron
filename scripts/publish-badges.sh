#!/usr/bin/env bash
# Publishes quality badges to the orphan `badges` branch. See docs/mutation-testing.md.
#
# usage: publish-badges.sh <coverage-badge.svg>
#   MUTATION_BADGE  optional path to a shields.io endpoint payload to publish
#                   as mutation.json. Omit it to leave any existing badge alone.
#
# Adds files to the branch rather than rebuilding it, so a run that publishes
# only the coverage badge leaves the mutation badge in place.
set -euo pipefail

coverage_badge="${1:?usage: publish-badges.sh <coverage-badge.svg>}"
[ -f "$coverage_badge" ] || { printf 'coverage badge not found: %s\n' "$coverage_badge" >&2; exit 1; }

# A branch name when on one, else the commit SHA. `rev-parse --abbrev-ref HEAD`
# is not safe here: it yields the literal "HEAD" on a detached checkout, which
# actions/checkout produces for some event types, and restoring to that would
# strand the work tree on the badges branch.
original_ref=$(git symbolic-ref --quiet --short HEAD || git rev-parse HEAD)
restore() {
  git reset --quiet 2>/dev/null || true
  git checkout --quiet "$original_ref" 2>/dev/null || echo "WARNING: could not restore $original_ref" >&2
}
trap restore EXIT

# This function force-pushes, so it must never confuse "the branch does not
# exist yet" with "I could not reach the remote". Getting that wrong either
# destroys remote commits (stale local ref + force) or recreates the branch from
# scratch (orphan + force), wiping the mutation badge this script exists to
# preserve.
#
# `ls-remote --exit-code` distinguishes the two: 0 found, 2 no matching ref,
# anything else a real failure.
set +e
git ls-remote --exit-code --heads origin badges >/dev/null 2>&1
remote_state=$?
set -e

case "$remote_state" in
  0)
    # --force so the local ref matches the remote exactly. A non-forcing
    # refspec is rejected when the two have diverged, and continuing from a
    # stale local branch would force-push over whatever the remote had.
    git fetch --force --quiet origin badges:badges
    git checkout --quiet --force badges
    echo 'badges branch checked out; existing artifacts preserved'
    ;;
  2)
    git checkout --quiet --orphan badges
    # An orphan checkout inherits the index, so clear the source tree it brought
    # with it. Fatal if it fails: otherwise the whole source tree gets committed
    # to the badges branch.
    git rm -rf . --quiet
    echo 'badges branch does not exist yet; creating it'
    ;;
  *)
    echo 'cannot reach origin to determine the state of the badges branch; refusing to publish' >&2
    exit 1
    ;;
esac

cp "$coverage_badge" coverage.svg
git add coverage.svg

# Validated, not merely existence-checked. A generator that fails after its
# output was redirected leaves a zero-byte or truncated file behind, and
# publishing that would destroy a working artifact and render as "invalid".
# Keeping the previous one is always better than overwriting it with junk.
publish_optional() {
  local payload="$1" dest="$2" filter="$3" what="$4"
  if [ -z "$payload" ] || [ ! -e "$payload" ]; then
    # Absent is not an error: it is how a pull-request run says "I have nothing
    # new for this artifact". A path that exists but is EMPTY is a different
    # thing — a generator that died mid-write — and is reported.
    printf 'no %s payload given; keeping any existing %s\n' "$what" "$dest"
  elif [ ! -s "$payload" ]; then
    printf '%s payload is empty; keeping any existing %s\n' "$what" "$dest" >&2
  elif ! jq -e "$filter" "$payload" >/dev/null 2>&1; then
    printf '%s payload is not usable; keeping any existing %s\n' "$what" "$dest" >&2
  else
    cp "$payload" "$dest"
    git add "$dest"
  fi
}

publish_optional "${MUTATION_BADGE:-}" mutation.json \
  'has("message") and has("schemaVersion")' 'mutation badge'

if git diff --staged --quiet; then
  echo 'badges unchanged'
else
  git commit --quiet -m 'chore: update quality badges'
  git push --force --quiet origin badges
fi
