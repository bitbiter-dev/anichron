#!/usr/bin/env bash
# Tests for ci-gate-guard.sh. Run: scripts/ci-gate-guard.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/ci-gate-guard.sh"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

# workflow <name>: stdin is the YAML; prints the file's path.
workflow() {
  cat >"$work/$1.yml"
  printf '%s' "$work/$1.yml"
}

assert_passes() {
  local label="$1" file="$2" out status
  out=$("$script" "$file" 2>&1); status=$?
  if [ "$status" -eq 0 ]; then pass "$label"; else fail "$label" 'exit 0' "exit $status with: $out"; fi
}

assert_fails() {
  local label="$1" needle="$2" file="$3" out status
  out=$("$script" "$file" 2>&1); status=$?
  if [ "$status" -eq 1 ] && [[ "$out" == *"$needle"* ]]; then
    pass "$label"
  else
    fail "$label" "exit 1 with output containing '$needle'" "exit $status with: $out"
  fi
}

wired=$(workflow wired <<'EOF'
on:
  pull_request:

jobs:
  changes:
    name: Changes
    runs-on: ubuntu-latest
    outputs:
      backend: ${{ steps.areas.outputs.backend }}
    steps:
      - id: areas
        run: echo backend=true >>"$GITHUB_OUTPUT"

  checks:
    name: Checks
    needs: changes
    if: needs.changes.outputs.backend == 'true'
    runs-on: ubuntu-latest
    steps:
      - run: dotnet test

  mutation:
    name: Mutation
    needs: [changes, checks]
    if: >-
      ${{ !cancelled()
      && needs.changes.outputs.backend == 'true' }}
    runs-on: ubuntu-latest
    steps:
      - if: needs.changes.outputs.backend == 'true'
        run: echo a step-level condition does not count

  ui:
    name: UI
    runs-on: ubuntu-latest
    steps:
      - run: pnpm test

  gate:
    name: CI gate
    needs:
      - changes
      - checks
      - mutation
      - ui
    if: always()
    runs-on: ubuntu-latest
    steps:
      - run: >-
          scripts/ci-gate.sh "$EVENT" "$NEEDS"
          --backend checks --backend mutation
EOF
)

echo "wired"

assert_passes 'the gate names exactly the jobs that skip with the backend' "$wired"

echo "drift"

assert_fails 'a backend job the gate does not name is reported' 'mutation' "$(workflow unnamed <<'EOF'
jobs:
  changes:
    runs-on: ubuntu-latest
  checks:
    needs: changes
    if: needs.changes.outputs.backend == 'true'
  mutation:
    needs: [changes]
    if: needs.changes.outputs.backend == 'true'
  gate:
    needs: [changes, checks, mutation]
    steps:
      - run: scripts/ci-gate.sh "$EVENT" "$NEEDS" --backend checks
EOF
)"

assert_fails 'a job the gate names but that never skips with the backend is reported' 'ui' "$(workflow overnamed <<'EOF'
jobs:
  changes:
    runs-on: ubuntu-latest
  ui:
    runs-on: ubuntu-latest
  gate:
    needs: [changes, ui]
    steps:
      - run: scripts/ci-gate.sh "$EVENT" "$NEEDS" --backend ui
EOF
)"

assert_fails 'a backend job without changes in its needs is reported' 'integration' "$(workflow no-changes-need <<'EOF'
jobs:
  changes:
    runs-on: ubuntu-latest
  integration:
    needs: checks
    if: needs.changes.outputs.backend == 'true'
  gate:
    needs: [changes, integration]
    steps:
      - run: scripts/ci-gate.sh "$EVENT" "$NEEDS" --backend integration
EOF
)"

assert_passes 'a backend job outside the gate needs no gate entry' "$(workflow outside-gate <<'EOF'
jobs:
  changes:
    runs-on: ubuntu-latest
  mutation-targets:
    needs: changes
    if: needs.changes.outputs.backend == 'true'
  gate:
    needs: [changes]
    steps:
      - run: scripts/ci-gate.sh "$EVENT" "$NEEDS"
EOF
)"

assert_fails 'a step-level backend condition does not make a backend job' 'checks' "$(workflow step-level <<'EOF'
jobs:
  changes:
    runs-on: ubuntu-latest
  checks:
    needs: changes
    steps:
      - if: needs.changes.outputs.backend == 'true'
        run: dotnet test
  gate:
    needs: [changes, checks]
    steps:
      - run: scripts/ci-gate.sh "$EVENT" "$NEEDS" --backend checks
EOF
)"

assert_fails 'a job that needs only a lookalike of changes is reported' 'integration' "$(workflow lookalike-need <<'EOF'
jobs:
  changes:
    runs-on: ubuntu-latest
  integration:
    needs: detect-changes
    if: needs.changes.outputs.backend == 'true'
  gate:
    needs: [changes, integration]
    steps:
      - run: scripts/ci-gate.sh "$EVENT" "$NEEDS" --backend integration
EOF
)"

assert_fails 'a condition that runs a job when the backend is unchanged does not make a backend job' 'checks' "$(workflow inverted <<'EOF'
jobs:
  changes:
    runs-on: ubuntu-latest
  checks:
    needs: changes
    if: needs.changes.outputs.backend != 'true'
  gate:
    needs: [changes, checks]
    steps:
      - run: scripts/ci-gate.sh "$EVENT" "$NEEDS" --backend checks
EOF
)"

echo "misconfiguration"

out=$("$script" "$work/missing.yml" 2>&1); status=$?
if [ "$status" -eq 2 ]; then pass 'a missing workflow is refused'; else fail 'a missing workflow is refused' 'exit 2' "exit $status with: $out"; fi

no_gate=$(workflow no-gate <<'EOF'
jobs:
  checks:
    runs-on: ubuntu-latest
EOF
)
out=$("$script" "$no_gate" 2>&1); status=$?
if [ "$status" -eq 2 ] && [[ "$out" == *gate* ]]; then pass 'a workflow without a gate job is refused'; else fail 'a workflow without a gate job is refused' 'exit 2 naming the gate' "exit $status with: $out"; fi

echo
if [ "$failures" -eq 0 ]; then
  echo 'all ci-gate-guard tests passed'
else
  echo "$failures ci-gate-guard test(s) failed"
  exit 1
fi
