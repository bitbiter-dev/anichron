#!/usr/bin/env bash
# Usage: scripts/mutation-stage-part.sh <reports-dir> <name> <parts-dir>
#
# Stages one project's mutation report for its upload: copies
# <reports-dir>/mutation-report.json to the place scripts/mutation-merge-parts.sh
# reads it from, <parts-dir>/mutation-part-<name>/. A run that left no report
# stages nothing, so the upload finds no file and the merge names the project.
#
# Exit 0: staged, or nothing to stage. Exit 1: a missing argument.
set -euo pipefail

if [ "$#" -ne 3 ] || [ -z "$2" ]; then
  echo "usage: scripts/mutation-stage-part.sh <reports-dir> <name> <parts-dir>" >&2
  exit 1
fi
reports="$1" name="$2" parts="$3"

[ -f "$reports/mutation-report.json" ] || exit 0
mkdir -p "$parts/mutation-part-$name"
cp "$reports/mutation-report.json" "$parts/mutation-part-$name/"
