#!/usr/bin/env bash
# Tests for graph-hint.sh. Run: scripts/graph-hint.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/graph-hint.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/graph-hint-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

git init -q "$work/repo"
mkdir -p "$work/repo/graphify-out"
cat >"$work/repo/graphify-out/graph.json" <<'EOF'
{"nodes": [
  {"label": "IProxyStore"},
  {"label": ".GetProxyAsync()"},
  {"label": "AssetViewReader"}
]}
EOF

# hook <tool> <input-field> <value> — a fresh session per call unless SESSION is set
hook() {
  jq -n --arg t "$1" --arg f "$2" --arg v "$3" --arg c "$work/repo" --arg s "${SESSION:-$RANDOM$RANDOM}" \
    '{tool_name: $t, session_id: $s, cwd: $c, tool_input: {($f): $v}}' |
    TMPDIR="$work" "$script"
}

expect_hint() {
  local label="$1" symbol="$2" out="$3"
  if [[ "$out" == *"scripts/graph.sh affected $symbol"* ]] && jq -e . >/dev/null 2>&1 <<<"$out"; then
    pass "$label"
  else
    fail "$label" "a hint for $symbol" "$out"
  fi
}

expect_silent() {
  local label="$1" out="$2"
  if [ -z "$out" ]; then pass "$label"; else fail "$label" 'no output' "$out"; fi
}

echo 'graph-hint.sh'

expect_hint 'grep for an interface' IProxyStore "$(hook Bash command 'grep -rn IProxyStore src')"
expect_hint 'grep after cd &&' AssetViewReader "$(hook Bash command 'cd /x && grep -rln "AssetViewReader" src --include=*.cs')"
expect_hint 'method call pattern' GetProxyAsync "$(hook Bash command 'grep -rn "\.GetProxyAsync(" src')"
expect_hint 'word-bounded pattern' IProxyStore "$(hook Bash command "grep -rn '\\bIProxyStore\\b' src")"
expect_hint 'rg with -g before the pattern' IProxyStore "$(hook Bash command 'rg -g "*.cs" IProxyStore')"
expect_hint 'git grep' IProxyStore "$(hook Bash command 'git grep -n IProxyStore')"
expect_hint 'grep -e' IProxyStore "$(hook Bash command 'grep -rn -e IProxyStore src')"
expect_hint 'grep after a pipe' IProxyStore "$(hook Bash command 'git ls-files | grep IProxyStore')"
expect_hint 'Grep tool' IProxyStore "$(hook Grep pattern 'IProxyStore')"

expect_silent 'not a search' "$(hook Bash command 'dotnet build src/')"
expect_silent 'all-caps text' "$(hook Bash command 'grep -rn TODO src')"
expect_silent 'lowercase text' "$(hook Bash command 'grep -rn proxies src')"
expect_silent 'regex, not a symbol' "$(hook Bash command 'grep -rnE "Proxy(Store|File)" src')"
expect_silent 'symbol unknown to the graph' "$(hook Bash command 'grep -rn NoSuchType src')"
expect_silent 'other tools' "$(hook Read file_path '/x/IProxyStore.cs')"
expect_silent 'malformed input' "$(echo 'not json' | TMPDIR="$work" "$script")"

SESSION=repeat
hook Bash command 'grep -rn IProxyStore src' >/dev/null
expect_silent 'same symbol twice in a session' "$(hook Bash command 'grep -rn IProxyStore src')"
expect_hint 'another symbol in the same session' AssetViewReader "$(hook Bash command 'grep -rn AssetViewReader src')"
unset SESSION

rm "$work/repo/graphify-out/graph.json"
expect_hint 'no graph built yet' SomeService "$(hook Bash command 'grep -rn SomeService src')"

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi
