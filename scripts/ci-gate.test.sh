#!/usr/bin/env bash
# Tests for ci-gate.sh. Run: scripts/ci-gate.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/ci-gate.sh"
failures=0

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

assert_contains() {
  case "$3" in
    *"$2"*) pass "$1" ;;
    *) fail "$1" "output containing '$2'" "$3" ;;
  esac
}

assert_passes() {
  local label="$1"; shift
  local out status
  out=$("$@" 2>&1); status=$?
  if [ "$status" -eq 0 ]; then pass "$label"; else fail "$label" 'exit 0' "exit $status with: $out"; fi
}

assert_fails() {
  local label="$1" needle="$2"; shift 2
  local out status
  out=$("$@" 2>&1); status=$?
  if [ "$status" -eq 0 ]; then
    fail "$label" "non-zero exit" "exit 0 with: $out"
  else
    assert_contains "$label" "$needle" "$out"
  fi
}

needs() {
  local json='{' sep='' pair
  for pair in "$@"; do
    json+="$sep\"${pair%%=*}\":{\"result\":\"${pair#*=}\",\"outputs\":{}}"
    sep=','
  done
  printf '%s}' "$json"
}

echo "pull requests"

assert_passes 'every job succeeded' \
  "$script" pull_request "$(needs checks=success mutation=success docker=success)" docker
assert_fails 'a failed job blocks' 'mutation: failure' \
  "$script" pull_request "$(needs checks=success mutation=failure docker=success)" docker
assert_fails 'a cancelled job blocks' 'checks: cancelled' \
  "$script" pull_request "$(needs checks=cancelled mutation=success docker=success)" docker
assert_fails 'a skipped job blocks' 'checks: skipped' \
  "$script" pull_request "$(needs checks=skipped mutation=success docker=success)" docker
assert_fails 'a skipped pull-request-only job blocks on a pull request' 'docker: skipped' \
  "$script" pull_request "$(needs checks=success mutation=success docker=skipped)" docker
assert_fails 'every blocking job is named, not only the first' 'mutation: failure' \
  "$script" pull_request "$(needs checks=failure mutation=failure docker=success)" docker

echo "pushes"

assert_passes 'a skipped pull-request-only job passes outside a pull request' \
  "$script" push "$(needs checks=success mutation=success docker=skipped)" docker
assert_passes 'a pull-request-only job that ran and succeeded still passes' \
  "$script" workflow_dispatch "$(needs checks=success mutation=success docker=success)" docker
assert_fails 'a failed pull-request-only job blocks outside a pull request' 'docker: failure' \
  "$script" push "$(needs checks=success mutation=success docker=failure)" docker
assert_fails 'a skipped job that is not pull-request-only blocks outside a pull request' 'mutation: skipped' \
  "$script" push "$(needs checks=success mutation=skipped docker=skipped)" docker
assert_fails 'with no pull-request-only jobs, a skip blocks on a push too' 'docker: skipped' \
  "$script" push "$(needs checks=success mutation=success docker=skipped)"

echo "misconfiguration"

assert_fails 'an empty needs object is refused' 'no jobs' \
  "$script" push '{}'
assert_fails 'input that is not JSON is refused' 'not a JSON object' \
  "$script" push 'not json'
assert_fails 'a pull-request-only job missing from needs is refused' 'dockr' \
  "$script" push "$(needs checks=success mutation=success docker=skipped)" dockr
assert_fails 'a missing event name is refused' 'usage' \
  "$script"

echo
if [ "$failures" -eq 0 ]; then
  echo 'all ci-gate tests passed'
else
  echo "$failures ci-gate test(s) failed"
  exit 1
fi
