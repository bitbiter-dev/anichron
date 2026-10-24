#!/usr/bin/env bash
# Tests for stryker-sweep.sh. Run: scripts/stryker-sweep.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/stryker-sweep.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/stryker-sweep-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

mkdir -p "$work/bin"
cat >"$work/bin/dotnet" <<'EOF'
#!/usr/bin/env bash
echo "$*" >>"$FAKE_CALLS"
case "$1" in
  restore) exit "${FAKE_RESTORE_STATUS:-0}" ;;
  stryker) printf '%b' "${FAKE_LOG:-}"; exit "${FAKE_STRYKER_STATUS:-0}" ;;
esac
EOF
chmod +x "$work/bin/dotnet"

clean_log='[08:52:01 WRN] Analysis of project Anichron.Core/Anichron.Core.csproj succeeded.\n[08:59:53 INF] The final mutation score is 100.00 %\n'
dropped_log='[08:52:01 WRN] Project Anichron.Worker/Anichron.Worker.csproj simulated build failed. Use the --diag option to have the build log.\n[08:52:01 WRN] Analysis of project Anichron.Worker/Anichron.Worker.csproj failed for frameworks net10.0.\n[08:59:53 INF] The final mutation score is 100.00 %\n'
test_project_log='[01:31:24 WRN] Analysis of project Anichron.Worker.Tests.Unit/Anichron.Worker.Tests.Unit.csproj succeeded but simulated build failed; Stryker may fail later.\n'

run() {
  : >"$work/calls"
  out=$(PATH="$work/bin:$PATH" FAKE_CALLS="$work/calls" "$script" "$@" 2>&1); status=$?
  calls=$(cat "$work/calls")
}

expect() {
  local label="$1" want_status="$2" needle="$3"
  if [ "$status" -ne "$want_status" ]; then
    fail "$label" "exit $want_status" "exit $status with: $out"
  elif [[ "$out" != *"$needle"* ]]; then
    fail "$label" "output containing '$needle'" "$out"
  else
    pass "$label"
  fi
}

echo 'stryker-sweep.sh'

FAKE_LOG="$clean_log" run --output ../mutation
expect 'a clean sweep exits 0 and shows the log' 0 'final mutation score is 100.00 %'
want=$'restore Anichron.slnx\nstryker --output ../mutation'
[ "$calls" = "$want" ] && pass 'restores before Stryker and passes its arguments through' \
  || fail 'restores before Stryker and passes its arguments through' "$want" "$calls"

FAKE_LOG="$clean_log" FAKE_STRYKER_STATUS=2 run
expect "Stryker's own failure status is kept" 2 'final mutation score'

FAKE_LOG="$dropped_log" run
expect 'a project Stryker could not analyse fails the sweep' 1 \
  'left a project unmutated: Analysis of project Anichron.Worker/Anichron.Worker.csproj failed'

FAKE_LOG="$dropped_log" FAKE_STRYKER_STATUS=2 run
expect 'a dropped project fails with 1 even when the score failed too' 1 'left a project unmutated'

FAKE_LOG="$test_project_log" run
expect 'a test project whose simulated build failed is not a dropped project' 0 'Stryker may fail later'

FAKE_RESTORE_STATUS=1 run
expect 'a failed restore fails the sweep' 1 ''
[ "$calls" = 'restore Anichron.slnx' ] && pass 'Stryker does not run after a failed restore' \
  || fail 'Stryker does not run after a failed restore' 'restore Anichron.slnx' "$calls"

if [ "$failures" -gt 0 ]; then
  printf '\n%d failure(s)\n' "$failures"
  exit 1
fi
printf '\nall passed\n'
