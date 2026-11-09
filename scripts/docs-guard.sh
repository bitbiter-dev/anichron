#!/usr/bin/env bash
# Usage: scripts/docs-guard.sh [file.md ...]
#
# Fails when a paragraph, list item or table cell in the repo's docs runs past
# 60 words. Without arguments it checks CLAUDE.md, docs/**/*.md (but not
# docs/agents/, which /setup-matt-pocock-skills writes) and .claude/docs/**/*.md.
# Code blocks are skipped.
#
# Exit 0: all within the limit. Exit 1: violations, one per line on stdout.
set -euo pipefail

root=$(git rev-parse --show-toplevel)
max=60

if [ "$#" -gt 0 ]; then
  files=("$@")
else
  files=()
  while IFS= read -r file; do files+=("$file"); done < <(cd "$root" && { echo CLAUDE.md; find docs .claude/docs -name "*.md" -not -path "docs/agents/*" 2>/dev/null | sort; })
fi

status=0
for file in "${files[@]}"; do
  out=$(cd "$root" && awk -v max="$max" -v file="$file" '
    function flush() {
      if (words > max) printf "%s:%d: %d words (limit %d)\n", file, start, words, max
      words = 0
    }
    /^[[:space:]]*```/ { flush(); fence = !fence; next }
    fence { next }
    /^[[:space:]]*$/ { flush(); next }
    /^[[:space:]]*#/ { flush(); next }
    /^[[:space:]]*\|/ {
      flush()
      n = split($0, cells, "|")
      for (i = 1; i <= n; i++) {
        c = split(cells[i], w, /[[:space:]]+/); k = 0
        for (j = 1; j <= c; j++) if (w[j] != "") k++
        if (k > max) printf "%s:%d: table cell of %d words (limit %d)\n", file, NR, k, max
      }
      next
    }
    /^[[:space:]]*([-*+]|[0-9]+\.)[[:space:]]/ { flush(); start = NR; words = NF - 1; next }
    { if (words == 0) start = NR; words += NF }
    END { flush() }
  ' "$file")
  if [ -n "$out" ]; then
    printf '%s\n' "$out"
    status=1
  fi
done
exit "$status"
