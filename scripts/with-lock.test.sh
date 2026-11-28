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

queue="$locks/heavy.queue"
tickets() { find "$queue" -mindepth 1 -maxdepth 1 2>/dev/null | wc -l | tr -d ' '; }

out=$(locked "$work/repo" heavy sh -c 'echo ran; exit 3' 2>&1); status=$?
if [ "$status" -eq 3 ] && [ "$out" = ran ]; then pass 'runs the command and returns its status'; else fail 'runs the command and returns its status' 'exit 3, ran' "exit $status, $out"; fi
if [ "$(tickets)" -eq 0 ]; then pass 'leaves the queue afterwards'; else fail 'leaves the queue afterwards' 'no ticket left' "$(ls -la "$queue")"; fi

mkfifo "$work/release"
locked "$work/repo" heavy sh -c "cat '$work/release' >/dev/null" &
first=$!
within_seconds 10 '[ "$(tickets)" -eq 2 ]' || fail 'the first caller takes the lock' 'a ticket' 'none'
mkfifo "$work/release-second"
locked "$work/other" heavy sh -c "cat '$work/release-second' >/dev/null; echo second >>'$work/order'" 2>"$work/waiting-second" &
second=$!
within_seconds 10 "grep -q 'waiting for the heavy lock' '$work/waiting-second' 2>/dev/null"
locked "$work/repo" heavy sh -c "echo third >>'$work/order'" 2>"$work/waiting-third" &
third=$!
within_seconds 10 "grep -q 'waiting for the heavy lock' '$work/waiting-third' 2>/dev/null"
if [ ! -e "$work/order" ]; then pass 'another worktree waits while the lock is held'; else fail 'another worktree waits while the lock is held' 'no second run yet' "$(cat "$work/order")"; fi
if grep -Eq "behind 1, held by process [0-9]+: sh -c cat '$work/release'" "$work/waiting-second"; then pass 'the wait names its place and the holding command'; else fail 'the wait names its place and the holding command' "behind 1, held by process <pid>: sh -c cat '$work/release'" "$(cat "$work/waiting-second")"; fi
if grep -Eq "behind 2, held by process [0-9]+: sh -c cat" "$work/waiting-third"; then pass 'a later caller counts everyone ahead'; else fail 'a later caller counts everyone ahead' 'behind 2' "$(cat "$work/waiting-third")"; fi
echo go 1<>"$work/release"
wait "$first"
if within_seconds 10 "grep -Eq \"behind 1, held by process [0-9]+: sh -c cat '\$work/release-second'\" '$work/waiting-third'"; then pass 'a waiter says when its place changes'; else fail 'a waiter says when its place changes' "behind 1, held by process <pid>: sh -c cat '$work/release-second'" "$(cat "$work/waiting-third")"; fi
echo go 1<>"$work/release-second"
wait "$second" "$third"
if [ "$(tr '\n' ' ' <"$work/order" 2>/dev/null)" = 'second third ' ]; then pass 'waiters run in the order they arrived'; else fail 'waiters run in the order they arrived' 'second third' "$(cat "$work/order" 2>/dev/null)"; fi

rm -f "$work/order"
locked "$work/repo" heavy sh -c "cat '$work/release' >/dev/null" &
first=$!
within_seconds 10 '[ "$(tickets)" -eq 2 ]'
locked "$work/other" heavy sh -c "echo failed >>'$work/order'; exit 1" 2>/dev/null &
second=$!
within_seconds 10 '[ "$(tickets)" -eq 4 ]'
locked "$work/repo" heavy sh -c "echo waiting >>'$work/order'" 2>/dev/null &
third=$!
within_seconds 10 '[ "$(tickets)" -eq 6 ]'
echo go 1<>"$work/release"
wait "$first"
wait "$second"
locked "$work/other" heavy sh -c "echo again >>'$work/order'" 2>/dev/null
wait "$third"
if [ "$(tr '\n' ' ' <"$work/order" 2>/dev/null)" = 'failed waiting again ' ]; then pass 'a caller that runs again joins the back'; else fail 'a caller that runs again joins the back' 'failed waiting again' "$(cat "$work/order" 2>/dev/null)"; fi

sh -c 'exit 0' &
dead=$!
wait "$dead"
mkdir -p "$queue"
ln -s "$dead@gone" "$queue/0000000001"
out=$(locked "$work/repo" heavy echo ran 2>&1); status=$?
if [ "$status" -eq 0 ] && [[ "$out" == *"skipped process $dead in the heavy queue, which has exited"* ]] && [[ "$out" == *ran ]] && [ "$(tickets)" -eq 0 ]; then
  pass 'a ticket whose process has exited is skipped and removed'
else
  fail 'a ticket whose process has exited is skipped and removed' "skipped $dead, then ran" "exit $status, $out"
fi

ln -s "$$@another-start" "$queue/0000000001"
out=$(locked "$work/repo" heavy echo ran 2>&1); status=$?
if [ "$status" -eq 0 ] && [[ "$out" == *"skipped process $$ in the heavy queue"* ]]; then
  pass 'a ticket whose PID now belongs to another process is skipped'
else
  fail 'a ticket whose PID now belongs to another process is skipped' "skipped $$" "exit $status, $out"
fi

locked "$work/repo" heavy sh -c "cat '$work/release' >/dev/null" &
first=$!
within_seconds 10 '[ "$(tickets)" -eq 2 ]'
(cd "$work/other" && WITH_LOCK_POLL_SECONDS=0 exec "$script" heavy echo never) 2>/dev/null &
waiter=$!
within_seconds 10 '[ "$(tickets)" -eq 4 ]'
kill -TERM "$waiter"
wait "$waiter"; status=$?
if [ "$status" -eq 143 ] && [ "$(tickets)" -eq 2 ]; then pass 'a waiter stopped by a signal leaves the queue'; else fail 'a waiter stopped by a signal leaves the queue' 'exit 143, only the holder left' "exit $status, $(ls "$queue")"; fi
echo go 1<>"$work/release"
wait "$first"

(cd "$work/repo" && WITH_LOCK_POLL_SECONDS=0 exec "$script" heavy sh -c "trap 'echo stopped >\"$work/stopped\"; exit 5' TERM; touch \"$work/started\"; while :; do sleep 0.1; done") &
holder=$!
within_seconds 10 '[ -e "$work/started" ]' || fail 'the signalled command starts' 'started' 'never'
kill -TERM "$holder"
wait "$holder"; status=$?
if [ "$status" -eq 5 ] && [ "$(cat "$work/stopped" 2>/dev/null)" = stopped ]; then pass 'a TERM reaches the command, and its status is returned'; else fail 'a TERM reaches the command, and its status is returned' 'exit 5, stopped' "exit $status, $(cat "$work/stopped" 2>/dev/null)"; fi
if [ "$(tickets)" -eq 0 ]; then pass 'the lock is released once the signalled command has exited'; else fail 'the lock is released once the signalled command has exited' 'no ticket' "$(ls "$queue")"; fi

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
