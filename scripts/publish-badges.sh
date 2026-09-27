#!/usr/bin/env bash
# Publishes quality badges to the orphan `badges` branch. See docs/mutation-testing.md.
#
# usage: publish-badges.sh <coverage-badge.svg>
#   MUTATION_BADGE  optional path to a shields.io endpoint payload to publish
#                   as mutation.json. Omit it to leave any existing badge alone.
#   HIGHWATER       optional path to a ratchet high-water mark to publish as
#                   mutation-highwater.json. Omit it to leave the mark alone —
#                   which is what every pull-request build does.
#
# Adds files to the branch rather than rebuilding it. An earlier version cleared
# the branch on every run, which would have deleted the mutation badge and the
# ratchet's stored high-water mark.
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
# scratch (orphan + force), wiping the mutation badge and #183's high-water mark
# — the very things this script exists to preserve.
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
#
# Extracted rather than copied for the second caller: the three-way distinction
# between absent, truncated and wrong-shaped is exactly the logic that must not
# drift between the badge and the high-water mark. Absent is not an error —
# it is the ordinary pull-request case for both.
# The optional fifth argument names a guard run after the payload validates. It
# is given the payload and the destination, may compare them, and returns
# non-zero to veto the write after explaining why on stderr.
publish_optional() {
  local payload="$1" dest="$2" filter="$3" what="$4" guard="${5:-}"
  if [ -z "$payload" ] || [ ! -e "$payload" ]; then
    # Absent is not an error for either caller: it is how a run says "I have
    # nothing new for this artifact". A path that exists but is EMPTY is a
    # different thing — a generator that died mid-write — and is reported.
    printf 'no %s payload given; keeping any existing %s\n' "$what" "$dest"
  elif [ ! -s "$payload" ]; then
    printf '%s payload is empty; keeping any existing %s\n' "$what" "$dest" >&2
  elif ! jq -e "$filter" "$payload" >/dev/null 2>&1; then
    printf '%s payload is not usable; keeping any existing %s\n' "$what" "$dest" >&2
  elif [ -n "$guard" ] && ! "$guard" "$payload" "$dest"; then
    : # the guard has explained itself
  else
    cp "$payload" "$dest"
    git add "$dest"
  fi
}

# Defence in depth at the last writer before the force-push, and the only place
# that can see the old and new marks at once.
#
# mutation-ratchet.sh already refuses to emit a lower mark, but it can only
# reason about the mark it was HANDED — and CI has to fetch that from this very
# branch. Anything that makes the read come back empty (an unreachable origin,
# a renamed branch) makes a seeded-from-the-floor advance look like a perfectly
# valid payload by the time it arrives here. This is the check that does not
# depend on the read having worked.
refuse_lower_mark() {
  local payload="$1" dest="$2" old new
  [ -f "$dest" ] || return 0
  old=$(jq -r 'if (.mark | type) == "number" then .mark else empty end' "$dest" 2>/dev/null) || return 0
  [ -n "$old" ] || return 0
  new=$(jq -r '.mark' "$payload")
  if jq -n -e --argjson a "$new" --argjson b "$old" '$a < $b' >/dev/null; then
    printf 'refusing to lower the high-water mark from %s to %s.\n' "$old" "$new" >&2
    printf 'Lowering it is a deliberate act; see docs/mutation-testing.md, "Lowering the mark by hand".\n' >&2
    return 1
  fi
}

publish_optional "${MUTATION_BADGE:-}" mutation.json \
  'has("message") and has("schemaVersion")' 'mutation badge'

# The ratchet's stored state (#183). It lives here rather than in the source
# tree so that raising the bar never means CI writing to the default branch.
publish_optional "${HIGHWATER:-}" mutation-highwater.json \
  '(.mark | type) == "number"' 'high-water mark' refuse_lower_mark

if git diff --staged --quiet; then
  echo 'badges unchanged'
else
  git commit --quiet -m 'chore: update quality badges'
  git push --force --quiet origin badges
fi
