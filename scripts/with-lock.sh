#!/usr/bin/env bash
# Usage: scripts/with-lock.sh <name> <command> [args...]
#
# Runs <command> holding the machine-wide lock <name>, shared by every worktree
# of this repository: a symlink under the common git dir naming the holder's
# PID and start time. Waits while that process lives, and takes over a lock
# whose process has gone, one waiter at a time. WITH_LOCK_POLL_SECONDS is the
# wait between checks (default 10).
#
# Exit: the command's status; 2 on a usage error.
set -euo pipefail

identity() { printf '%s@%s' "$1" "$(ps -o lstart= -p "$1" 2>/dev/null | tr -s ' ' _)"; }

alive() { [ "$(identity "${1%%@*}")" = "$1" ]; }

main() {
  if [ "$#" -lt 2 ]; then
    echo 'usage: with-lock.sh <name> <command> [args...]' >&2
    exit 2
  fi
  local name="$1"
  shift
  case "$name" in
    '' | *[!A-Za-z0-9_-]*) echo "invalid lock name: '$name'" >&2; exit 2 ;;
  esac

  local dir lock poll="${WITH_LOCK_POLL_SECONDS:-10}" me holder announced=''
  dir="$(git rev-parse --path-format=absolute --git-common-dir)/anichron-locks"
  lock="$dir/$name"
  mkdir -p "$dir"
  me=$(identity $$)

  until ln -s "$me" "$lock" 2>/dev/null; do
    holder=$(readlink "$lock" 2>/dev/null) || continue
    if ! alive "$holder"; then
      if mkdir "$lock.takeover" 2>/dev/null; then
        if [ "$(readlink "$lock" 2>/dev/null)" = "$holder" ]; then
          rm -f "$lock"
          echo "took over the $name lock from process ${holder%%@*}, which has exited" >&2
        fi
        rmdir "$lock.takeover"
      else
        sleep "$poll"
      fi
      continue
    fi
    if [ "$announced" != "$holder" ]; then
      echo "waiting for the $name lock, held by process ${holder%%@*}: $(cat "$lock.command" 2>/dev/null)" >&2
      announced="$holder"
    fi
    sleep "$poll"
  done

  printf '%s\n' "$*" >"$lock.command"
  trap 'rm -f "$lock" "$lock.command"' EXIT
  trap 'exit 130' INT
  trap 'exit 143' TERM

  local status=0
  "$@" || status=$?
  exit "$status"
}

main "$@"
