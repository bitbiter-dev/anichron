#!/usr/bin/env bash
# Tests for with-lock.sh. Run: scripts/with-lock.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/with-lock.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/with-lock-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

git init -q "$work/repo"
git -C "$work/repo" -c user.name=mira -c user.email=mira@example.org commit -q --allow-empty -m base
git -C "$work/repo" worktree add -q "$work/other" -b other
locks="$work/repo/.git/anichron-locks"

locked() { (cd "$1" && WITH_LOCK_POLL_SECONDS=0 "$script" "${@:2}"); }

# Fails the test instead of spinning when <condition> never holds.
within_seconds() {
  local deadline=$((SECONDS + $1))
  until eval "$2"; do
    [ "$SECONDS" -lt "$deadline" ] || return 1
  done
}

echo 'with-lock.sh'

out=$(locked "$work/repo" heavy sh -c 'echo ran; exit 3' 2>&1); status=$?
if [ "$status" -eq 3 ] && [ "$out" = ran ]; then pass 'runs the command and returns its status'; else fail 'runs the command and returns its status' 'exit 3, ran' "exit $status, $out"; fi
if [ ! -e "$locks/heavy" ] && [ ! -e "$locks/heavy.command" ]; then pass 'releases the lock afterwards'; else fail 'releases the lock afterwards' 'no lock left' "$(ls -la "$locks")"; fi

mkfifo "$work/release"
locked "$work/repo" heavy sh -c "cat '$work/release' >/dev/null" &
first=$!
within_seconds 10 '[ -s "$locks/heavy.command" ]' || fail 'the first caller takes the lock' 'a lock' 'none'
locked "$work/other" heavy sh -c "echo second >'$work/second'" 2>"$work/waiting" &
second=$!
within_seconds 10 "grep -q 'waiting for the heavy lock' '$work/waiting' 2>/dev/null || [ -e '$work/second' ]"
if [ ! -e "$work/second" ]; then pass 'another worktree waits while the lock is held'; else fail 'another worktree waits while the lock is held' 'no second run yet' 'it ran'; fi
if grep -qF "$work/release" "$work/waiting"; then pass 'the wait names the holding command'; else fail 'the wait names the holding command' "$work/release" "$(cat "$work/waiting")"; fi
echo go 1<>"$work/release"
wait "$first"
wait "$second"
if [ "$(cat "$work/second" 2>/dev/null)" = second ]; then pass 'it runs once the lock is released'; else fail 'it runs once the lock is released' second "$(cat "$work/second" 2>/dev/null)"; fi

sh -c 'exit 0' &
dead=$!
wait "$dead"
mkdir -p "$locks"
ln -s "$dead@gone" "$locks/heavy"
out=$(locked "$work/repo" heavy echo ran 2>&1); status=$?
if [ "$status" -eq 0 ] && [[ "$out" == *"took over the heavy lock from process $dead"* ]] && [[ "$out" == *ran ]]; then
  pass 'a lock whose process has exited is taken over'
else
  fail 'a lock whose process has exited is taken over' "takeover of $dead, then ran" "exit $status, $out"
fi

ln -s "$$@another-start" "$locks/heavy"
out=$(locked "$work/repo" heavy echo ran 2>&1); status=$?
if [ "$status" -eq 0 ] && [[ "$out" == *"took over the heavy lock from process $$"* ]]; then
  pass 'a lock whose PID now belongs to another process is taken over'
else
  fail 'a lock whose PID now belongs to another process is taken over' "takeover of $$" "exit $status, $out"
fi

(cd "$work/repo" && WITH_LOCK_POLL_SECONDS=0 exec "$script" heavy sh -c "trap 'echo stopped >\"$work/stopped\"; exit 5' TERM; touch \"$work/started\"; while :; do sleep 0.1; done") &
holder=$!
within_seconds 10 '[ -e "$work/started" ]' || fail 'the signalled command starts' 'started' 'never'
kill -TERM "$holder"
wait "$holder"; status=$?
if [ "$status" -eq 5 ] && [ "$(cat "$work/stopped" 2>/dev/null)" = stopped ]; then pass 'a TERM reaches the command, and its status is returned'; else fail 'a TERM reaches the command, and its status is returned' 'exit 5, stopped' "exit $status, $(cat "$work/stopped" 2>/dev/null)"; fi
if [ ! -e "$locks/heavy" ]; then pass 'the lock is released once the signalled command has exited'; else fail 'the lock is released once the signalled command has exited' 'no lock' "$(readlink "$locks/heavy")"; fi

mkdir -p "$locks/heavy.takeover"
touch -t 202601010000 "$locks/heavy.takeover"
ln -s "$dead@gone" "$locks/heavy"
out=$(locked "$work/repo" heavy echo ran 2>&1); status=$?
if [ "$status" -eq 0 ] && [[ "$out" == *'removed a takeover'* ]] && [[ "$out" == *ran ]]; then pass 'a takeover left by a waiter that died is cleared'; else fail 'a takeover left by a waiter that died is cleared' 'cleared, then ran' "exit $status, $out"; fi

out=$(locked "$work/repo" other echo ran 2>&1)
if [ "$out" = ran ]; then pass 'locks with other names are independent'; else fail 'locks with other names are independent' ran "$out"; fi

out=$(locked "$work/repo" 'a/b' echo ran 2>&1); status=$?
if [ "$status" -eq 2 ] && [[ "$out" == *'invalid lock name'* ]]; then pass 'a name with a slash is a usage error'; else fail 'a name with a slash is a usage error' 'exit 2' "exit $status, $out"; fi

out=$(locked "$work/repo" heavy 2>&1); status=$?
if [ "$status" -eq 2 ]; then pass 'no command is a usage error'; else fail 'no command is a usage error' 'exit 2' "exit $status, $out"; fi

if [ "$failures" -gt 0 ]; then
  printf '\n%d failure(s)\n' "$failures"
  exit 1
fi
printf '\nall passed\n'
