#!/usr/bin/env bash
# Usage: scripts/full-run-report.sh <source> <run-id> <assignee>
#
# Reports a full run's outcome on the issue "Full run failed: <source>", from
# the run's finished jobs. A job that failed, was cancelled or timed out opens
# the issue, assigned to <assignee> in the Engineering Process milestone and
# labelled ready-for-agent, or comments on the open one. A run with no such job
# closes the open one. Needs `gh` authenticated (GH_TOKEN in CI).
set -euo pipefail

if [ "$#" -ne 3 ]; then
  echo 'usage: full-run-report.sh <source> <run-id> <assignee>' >&2
  exit 2
fi
source_name="$1"
run_id="$2"
assignee="$3"
title="Full run failed: $source_name"

run=$(gh run view "$run_id" --json url,jobs) || { echo "cannot read run $run_id" >&2; exit 1; }
url=$(jq -r .url <<<"$run")
failed=$(jq -r '.jobs[] | select(.conclusion == "failure" or .conclusion == "cancelled" or .conclusion == "timed_out") | "- `\(.name)`: \(.conclusion)"' <<<"$run")

open=$(gh issue list --state open --limit 1000 --json number,title \
  | jq -r --arg title "$title" '[.[] | select(.title == $title)][0].number // empty')

if [ -z "$failed" ]; then
  if [ -n "$open" ]; then
    gh issue close "$open" --comment "Passed again: $url"
    echo "closed #$open"
  else
    echo "passed: $source_name"
  fi
  exit 0
fi

if [ -n "$open" ]; then
  gh issue comment "$open" --body-file - <<EOF
Failed again: $url

$failed
EOF
  echo "commented on #$open"
  exit 0
fi

gh issue create --title "$title" --label ready-for-agent --milestone 'Engineering Process' \
  --assignee "$assignee" --body-file - <<EOF
The full run $url failed:

$failed

## What to build

A fix that makes the next run of $source_name pass. The run's summary lists the surviving mutants or the failing step, and \`gh run view $run_id --log-failed\` shows the log.

## First caller

The next run of $source_name. The PR with the fix closes this issue.

## Design

Design: not needed because this restores a gate that already exists.
EOF
echo "opened: $title"
