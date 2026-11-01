#!/usr/bin/env bash
# Usage: lfs-pointers.sh
#
# Exits 0 when every file in the index is text or a Git LFS pointer. Otherwise exits 1 and
# names each binary stored as an ordinary blob, whatever its extension. It reads the index,
# so the pre-commit hook checks what is staged and CI checks the commit, without the LFS
# objects.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

binaries=()
while IFS= read -r -d '' entry; do
  case "$entry" in
    i/-text*) binaries+=("${entry#*$'\t'}") ;;
  esac
done < <(git ls-files --eol -z)

if [ "${#binaries[@]}" -gt 0 ]; then
  echo 'These binary files are not Git LFS pointers:' >&2
  printf '  %s\n' "${binaries[@]}" >&2
  echo 'Add their type to .gitattributes (filter=lfs diff=lfs merge=lfs -text), then run' >&2
  echo 'git add --renormalize <path>. Without git-lfs installed, see CLAUDE.md, "First-time setup".' >&2
  exit 1
fi
