#!/usr/bin/env bash
# Tests for arch-exceptions-check.sh. Run: scripts/arch-exceptions-check.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/arch-exceptions-check.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/arch-exceptions-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

mkdir -p "$work/bin"
cat >"$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
echo "$*" >>"$GH_CALLS"
case " $* " in
  *" 10 "*) echo OPEN ;;
  *" 20 "*) echo CLOSED ;;
  *) echo "unexpected gh call: $*" >&2; exit 9 ;;
esac
EOF
chmod +x "$work/bin/gh"

run() {
  printf '%s' "$1" >"$work/exceptions.json"
  : >"$work/calls"
  GH_CALLS="$work/calls" PATH="$work/bin:$PATH" "$script" "$work/exceptions.json" 2>&1
}

check() {
  local label="$1" json="$2" want_status="$3" needle="$4" out status
  out=$(run "$json"); status=$?
  if [ "$status" -ne "$want_status" ]; then
    fail "$label" "exit $want_status" "exit $status with: $out"
  elif [[ "$out" != *"$needle"* ]]; then
    fail "$label" "output containing '$needle'" "$out"
  else
    pass "$label"
  fi
}

echo 'arch-exceptions-check.sh'
check 'no exceptions passes' '[]' 0 'every architecture exception names an open issue'
check 'an open issue passes' \
  '[{"rule":"r","type":"A.B","issue":10,"reason":"x"}]' 0 'every architecture exception names an open issue'
check 'a closed issue fails and names the entry' \
  '[{"rule":"r","type":"A.B","issue":10,"reason":"x"},{"rule":"q","type":"C.D","issue":20,"reason":"y"}]' 1 '#20  q  C.D'
check 'two exceptions on one open issue pass' \
  '[{"rule":"r","type":"A.B","issue":10,"reason":"x"},{"rule":"r","type":"E.F","issue":10,"reason":"x"}]' 0 'every architecture exception names an open issue'
lookups=$(wc -l <"$work/calls" | tr -d ' ')
if [ "$lookups" = 1 ]; then pass 'each issue is looked up once'; else fail 'each issue is looked up once' '1 gh call' "$lookups gh calls"; fi
check 'unreadable json is a usage error' 'not json' 2 'cannot read exceptions'
check 'a failed issue lookup is an error, not a closed issue' \
  '[{"rule":"r","type":"A.B","issue":30,"reason":"x"}]' 2 'cannot read the state of issue #30'

if [ "$failures" -gt 0 ]; then
  printf '\n%d failure(s)\n' "$failures"
  exit 1
fi
printf '\nall passed\n'
