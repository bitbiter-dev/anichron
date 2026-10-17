#!/usr/bin/env bash
# Claude Code PreToolUse hook (Bash|Grep), wired in .claude/settings.json.
#
# When a search looks up the references of one C# symbol (grep/rg/git grep for
# a PascalCase identifier), adds a one-line hint pointing at
# `scripts/graph.sh affected|explain <Symbol>`. Silent for anything else, for a
# symbol the graph does not know, and for a symbol already hinted in this
# session. Never blocks the tool call.
set -uo pipefail

input=$(cat)
tool=$(jq -r '.tool_name // empty' <<<"$input" 2>/dev/null) || exit 0
session=$(jq -r '.session_id // "none"' <<<"$input")
cwd=$(jq -r '.cwd // empty' <<<"$input")

candidates=()
case "$tool" in
  Grep)
    candidates+=("$(jq -r '.tool_input.pattern // empty' <<<"$input")")
    ;;
  Bash)
    command=$(jq -r '.tool_input.command // empty' <<<"$input")
    command=${command//&&/$'\n'}
    command=${command//||/$'\n'}
    command=${command//[;|]/$'\n'}
    while IFS= read -r segment; do
      read -ra words <<<"$segment"
      [ "${#words[@]}" -eq 0 ] && continue
      i=0
      case "${words[0]}" in
        grep|egrep|rg) i=1 ;;
        git) [ "${words[1]:-}" = grep ] && i=2 ;;
      esac
      [ "$i" -eq 0 ] && continue
      while [ "$i" -lt "${#words[@]}" ]; do
        word=${words[$i]}
        case "$word" in
          -e|--regexp) candidates+=("${words[$((i + 1))]:-}"); break ;;
          -A|-B|-C|-m|-g|-t|--glob|--type|--max-count) i=$((i + 2)) ;;
          -*) i=$((i + 1)) ;;
          *) candidates+=("$word"); break ;;
        esac
      done
    done <<<"$command"
    ;;
  *) exit 0 ;;
esac
[ "${#candidates[@]}" -eq 0 ] && exit 0

root=$(git -C "${cwd:-.}" rev-parse --show-toplevel 2>/dev/null) || exit 0
graph="$root/graphify-out/graph.json"
seen="${TMPDIR:-/tmp}/anichron-graph-hint-$session"

for raw in "${candidates[@]}"; do
  symbol=$(sed -E "s/^['\"]//; s/['\"]$//; s/\\\\[b<>]//g; s/^\\\\?\\.//; s/(\\\\?\\()$//" <<<"$raw")
  [[ "$symbol" =~ ^[A-Z][A-Za-z0-9_]*[a-z][A-Za-z0-9_]*$ ]] || continue
  [ "${#symbol}" -ge 4 ] || continue
  if [ -f "$graph" ] && ! grep -qE "\"label\": \"\\.?${symbol}(\\(\\))?\"" "$graph"; then
    continue
  fi
  grep -qxF "$symbol" "$seen" 2>/dev/null && continue
  echo "$symbol" >>"$seen"
  jq -n --arg s "$symbol" '{hookSpecificOutput: {hookEventName: "PreToolUse", additionalContext:
    "For the references of `\($s)`, `scripts/graph.sh affected \($s)` lists everything that calls, references or implements it (tests included, depth 2, file:line); `scripts/graph.sh explain \($s)` lists its direct callers and callees."}}'
  exit 0
done
exit 0
