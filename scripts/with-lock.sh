#!/usr/bin/env bash
# Usage: scripts/with-lock.sh <name> <command> [args...]
#
# Runs <command> holding the machine-wide lock <name>, shared by every worktree
# of this repository and handed out in the order callers arrive. Each caller
# takes the next ticket in a queue under the common git dir: a symlink naming
# its PID and start time, beside a <pid>.command file naming its command. It waits while a
# live ticket is ahead of it, saying its place and the holder whenever they
# change, and removes the tickets of processes that have gone. Its own ticket
# goes when it exits, so a caller that runs again joins the back. TERM, HUP and
# INT go to the command, and the lock is released only once it has exited.
# WITH_LOCK_POLL_SECONDS is the wait between checks (default 10).
#
# Exit: the command's status; 2 on a usage error.
set -euo pipefail

identity() { printf '%s@%s' "$1" "$(ps -o lstart= -p "$1" 2>/dev/null | tr -s ' ' _)"; }

alive() { [ "$(identity "${1%%@*}")" = "$1" ]; }

# The queue's tickets, oldest first.
tickets() { ls "$queue" | { grep -E '^[0-9]+$' || true; } | sort -n; }

# Takes the ticket after the newest one; sets `ticket`.
take_ticket() {
  local newest next
  while :; do
    newest=$(tickets | tail -n 1)
    next=$(printf '%010d' $((10#${newest:-0} + 1)))
    if ln -s "$me" "$queue/$next" 2>/dev/null; then
      ticket=$next
      return
    fi
  done
}

# Sets `ahead` to the live tickets before ours, removing those of processes
# that have gone.
read_ahead() {
  local t holder
  ahead=()
  for t in $(tickets); do
    [ "$t" != "$ticket" ] || return 0
    holder=$(readlink "$queue/$t" 2>/dev/null) || continue
    if alive "$holder"; then
      ahead+=("$t")
    elif rm "$queue/$t" 2>/dev/null; then
      rm -f "$queue/${holder%%@*}.command"
      echo "with-lock: skipped process ${holder%%@*} in the $name queue, which has exited" >&2
    fi
  done
}

main() {
  if [ "$#" -lt 2 ]; then
    echo 'usage: with-lock.sh <name> <command> [args...]' >&2
    exit 2
  fi
  name="$1"
  shift
  case "$name" in
    '' | *[!A-Za-z0-9_-]*) echo "with-lock: invalid lock name: '$name'" >&2; exit 2 ;;
  esac

  local poll="${WITH_LOCK_POLL_SECONDS:-10}" announced='' place holder
  queue="$(git rev-parse --path-format=absolute --git-common-dir)/anichron-locks/$name.queue"
  mkdir -p "$queue"
  me=$(identity $$)
  ticket=''

  trap 'rm -f "$queue/$$.command"; [ -z "$ticket" ] || rm -f "$queue/$ticket"' EXIT
  trap 'exit 143' TERM HUP INT
  printf '%s\n' "$*" >"$queue/$$.command"
  take_ticket

  while read_ahead; [ "${#ahead[@]}" -gt 0 ]; do
    holder=$(readlink "$queue/${ahead[0]}" 2>/dev/null) || continue
    place="behind ${#ahead[@]}, held by process ${holder%%@*}: $(cat "$queue/${holder%%@*}.command" 2>/dev/null || true)"
    if [ "$announced" != "$place" ]; then
      echo "with-lock: waiting for the $name lock, $place" >&2
      announced="$place"
    fi
    sleep "$poll"
  done

  local child status=0
  "$@" &
  child=$!
  trap 'kill -TERM "$child" 2>/dev/null || true' TERM HUP INT
  while kill -0 "$child" 2>/dev/null; do
    wait "$child" || true
  done
  wait "$child" || status=$?
  exit "$status"
}

main "$@"
