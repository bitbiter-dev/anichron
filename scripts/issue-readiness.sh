#!/usr/bin/env bash
# Usage: scripts/issue-readiness.sh < issue.json
#
# Reads `gh issue view <n> --json number,labels,body,comments` on stdin and
# checks that a `ready-for-agent` issue carries the parts that
# docs/agents/design-process.md ("What every issue carries") requires: a first
# caller and a design part, in the issue body or in an `## Agent Brief` comment.
#
# Exit 0: ready, or not subject to the check (not ready-for-agent, an epic, or a
#         legacy issue up to #261). Exit 1: parts missing, one per line on
#         stdout. Exit 2: unreadable input.
set -euo pipefail

legacy_cutoff=261

if ! issue=$(jq -e 'select(type == "object" and has("number"))' 2>/dev/null); then
  echo 'expected the JSON of `gh issue view --json number,labels,body,comments`' >&2
  exit 2
fi

labels=$(jq -r '[.labels[]?.name] | join("\n")' <<<"$issue")
number=$(jq -r '.number' <<<"$issue")

grep -qx 'ready-for-agent' <<<"$labels" || exit 0
grep -q '^epic:' <<<"$labels" && exit 0
[ "$number" -le "$legacy_cutoff" ] && exit 0

texts=$(jq -r '[.body // ""] + [.comments[]?.body | select(test("(?m)^## Agent Brief"))] | join("\n\n## \n\n")' <<<"$issue")

# The text of a `## <name>` section and of every `**<name>:**` field line,
# matched case-insensitively, with an optional trailing colon on the heading and
# an optional list marker before the field.
part() {
  awk -v name="$(tr '[:upper:]' '[:lower:]' <<<"$1")" '
    BEGIN {
      heading = "^##[[:space:]]+" name ":?[[:space:]]*$"
      field = "^[[:space:]]*([-*][[:space:]]+)?\\*\\*" name ":?\\*\\*:?"
    }
    { line = tolower($0) }
    line ~ heading { inside = 1; next }
    inside && /^##?[[:space:]]/ { inside = 0 }
    inside { print; next }
    line ~ field { match(line, field); print substr($0, RLENGTH + 1) }
  ' <<<"$texts"
}

missing=()

if ! grep -q '[^[:space:]]' <<<"$(part 'First caller')"; then
  missing+=('First caller: a non-empty "## First caller" section or "**First caller:**" field')
fi

design=$(part 'Design')
if ! grep -Eiq '(^|[^[:alnum:]])not needed because[[:space:]]+[^[:space:]]+[[:space:]]+[^[:space:]]|#issuecomment-[0-9]+|docs/adr/[0-9]{4}-' <<<"$design"; then
  missing+=('Design: a "## Design" section or "**Design:**" field with a link to the approved design note (an #issuecomment link) or an ADR, or "Design: not needed because <reason>"')
fi

if [ "${#missing[@]}" -gt 0 ]; then
  printf '%s\n' "${missing[@]}"
  exit 1
fi
