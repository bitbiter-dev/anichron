#!/usr/bin/env bash
# Tests for graph.sh. Run: scripts/graph.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/graph.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/graph-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

mkdir -p "$work/bin"
cat >"$work/bin/graphify" <<'EOF'
#!/usr/bin/env bash
echo "$*" >>"$GRAPHIFY_CALLS"
if [ "$1" = update ]; then
  [ -n "${GRAPHIFY_FAIL:-}" ] && { echo 'extraction blew up'; exit 1; }
  mkdir -p graphify-out && echo '{}' >graphify-out/graph.json
else
  echo "answer for $*"
fi
EOF
chmod +x "$work/bin/graphify"

repo="$work/repo"
git init -q "$repo"
mkdir -p "$repo/src/Anichron.Core/bin" "$repo/docs"
echo 'class A {}' >"$repo/src/Anichron.Core/A.cs"

run() {
  : >"$work/calls"
  (cd "$repo/src" && GRAPHIFY_CALLS="$work/calls" PATH="$work/bin:$PATH" "$script" "$@" 2>&1)
}

expect() {
  local label="$1" want_status="$2" needle="$3" want_calls="$4" out="$5" status="$6"
  local calls
  calls=$(tr '\n' ';' <"$work/calls")
  if [ "$status" -ne "$want_status" ]; then
    fail "$label" "exit $want_status" "exit $status with: $out"
  elif [[ "$out" != *"$needle"* ]]; then
    fail "$label" "output containing '$needle'" "$out"
  elif [ "$calls" != "$want_calls" ]; then
    fail "$label" "graphify calls '$want_calls'" "'$calls'"
  else
    pass "$label"
  fi
}

age() { touch -t 202001010000 "$@"; }

echo 'graph.sh'

out=$(run explain A); expect 'no graph: builds it, then answers' 0 'answer for explain A' 'update . --force;explain A;' "$out" $?

out=$(run affected A); expect 'fresh graph: answers without building' 0 'answer for affected A' 'affected A;' "$out" $?

age "$repo/graphify-out/graph.json"
out=$(run explain A); expect 'source newer than graph: rebuilds' 0 'answer for explain A' 'update . --force;explain A;' "$out" $?

age "$repo/graphify-out/graph.json" "$repo/src/Anichron.Core/A.cs"
echo x >"$repo/src/Anichron.Core/bin/A.dll"
out=$(run explain A); expect 'build output newer than graph: no rebuild' 0 'answer for explain A' 'explain A;' "$out" $?

rm "$repo/graphify-out/graph.json"
: >"$work/calls"
out=$(cd "$repo" && GRAPHIFY_FAIL=1 GRAPHIFY_CALLS="$work/calls" PATH="$work/bin:$PATH" "$script" explain A 2>&1)
expect 'failed build: exits 1 with the log' 1 'extraction blew up' 'update . --force;' "$out" $?

: >"$work/calls"
out=$(cd "$repo" && PATH="/usr/bin:/bin" "$script" explain A 2>&1)
expect 'graphify missing: exits 127 with install hint' 127 'uv tool install graphifyy' '' "$out" $?

: >"$work/calls"
out=$(cd "$repo" && PATH="$work/bin:$PATH" "$script" 2>&1)
expect 'no arguments: prints usage' 0 'Usage: scripts/graph.sh' '' "$out" $?

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi
