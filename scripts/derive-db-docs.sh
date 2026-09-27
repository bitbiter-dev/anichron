#!/usr/bin/env bash
# Regenerates docs/schema.sql — the DDL EF Core will actually create.
#
# usage: scripts/derive-db-docs.sh [--check]
#   --check   regenerate and FAIL if that changed anything. This is what CI runs.
#
# ⭐ This script CALLS a tool; it renders nothing itself. `dotnet ef dbcontext script` is the
# same machinery that produces the migrations, so its output cannot disagree with them. An
# earlier version hand-rendered Markdown from the EF model in ~200 lines of C# — a third copy
# of facts that already existed in the migration snapshot, which is the very duplication that
# let the old documentation rot in the first place.
#
# ⭐ The table and column COMMENTs in the output are not decoration: the rationale lives in
# AnichronDbContext beside the Fluent config it explains, travels into PostgreSQL, and is
# therefore picked up by any schema-documentation tool pointed at the database. Prose kept in a
# separate file is prose that can disagree with the schema; prose kept IN the schema cannot.
#
# ⚠️ No database is contacted. `dbcontext script` works off the model, so this runs anywhere the
# SDK runs — including a runner with no Docker daemon. A browsable ER diagram needs a live
# database and is a separate CI job whose output is an artefact, not a committed file.
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"

target="docs/schema.sql"
check=false
[ "${1:-}" = "--check" ] && check=true

mkdir -p "$(dirname "$target")"

# stdout only: the tool writes a tools-version notice to stderr, which is not part of the schema.
dotnet ef dbcontext script --project src/Anichron.Core --no-build 2>/dev/null > "$target"

# ⛔ Regenerate-then-ask-git, rather than diffing against a temp file. Process substitution hands
# diff a /dev/fd path that some sandboxes and container runtimes refuse, and a fixed temp path
# collides between concurrent runs — both fail in ways that read as a broken script rather than
# as drift. Regenerating in place needs neither, gives a real diff for free, and is the pattern
# tbls and Rails' schema.rb checks both use.
#
# `status --porcelain` rather than `diff --quiet`: the latter reports no change for a file git
# does not yet track, so a newly added schema would pass the gate silently.
if ! command -v git >/dev/null 2>&1 || ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  printf '%s regenerated. (No git work tree — cannot report whether it changed.)\n' "$target"
  exit 0
fi

changed=$(git status --porcelain -- "$target")

if [ "$check" = true ]; then
  if [ -n "$changed" ]; then
    printf '%s is stale.\n\n' "$target" >&2
    # Show WHAT moved. A schema change is exactly the moment a reviewer wants the diff, and
    # making them re-run the command to see it is how a drift gate becomes something people
    # switch off. `git diff` is empty for an untracked file, hence the fallback.
    if git ls-files --error-unmatch "$target" >/dev/null 2>&1; then
      git --no-pager diff -- "$target" >&2
    else
      printf '(%s is not tracked yet — commit it.)\n' "$target" >&2
    fi
    printf '\nRun scripts/derive-db-docs.sh and commit the result.\n' >&2
    exit 1
  fi
  printf '%s is current.\n' "$target"
  exit 0
fi

printf '%s regenerated from the EF Core model.\n' "$target"
if [ -z "$changed" ]; then
  printf 'No change — the committed file already matched the model.\n'
else
  printf 'The file CHANGED. Review and commit it:\n  git add %s && git commit\n' "$target"
fi
