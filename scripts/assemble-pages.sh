#!/usr/bin/env bash
# Assembles the GitHub Pages site holding both quality reports. See docs/mutation-testing.md.
#
# usage: assemble-pages.sh <coverage-report-dir> <mutation-report-dir> <output-dir>
#
# Produces, under <output-dir>:
#   index.html    a landing page linking whichever reports are present
#   coverage/     the coverage report
#   mutation/     the mutation report, when one was produced
#   .nojekyll     so Pages serves paths beginning with an underscore
#
# The coverage report moves out of the site root into coverage/ so the root can
# be a landing page instead of arbitrarily serving one of the two reports. That
# changes the existing coverage URL, which #181 accepts deliberately.
#
# The mutation report is OPTIONAL and its absence is not an error. Stryker runs
# under continue-on-error, so a crash leaves no HTML report; failing here would
# fail the required check a second time for a publishing-only reason, and would
# publish nothing at all -- including the coverage report, which used to ship
# regardless. This reverses the "both reports or neither" rule an earlier draft
# of this script carried: a missing report is honest, a stale one is misleading,
# and neither is worth blocking a merge over. The landing page says which is
# missing rather than pretending the site is complete.
set -euo pipefail

coverage_dir="${1:?usage: assemble-pages.sh <coverage-report-dir> <mutation-report-dir> <output-dir>}"
mutation_dir="${2:?usage: assemble-pages.sh <coverage-report-dir> <mutation-report-dir> <output-dir>}"
output="${3:?usage: assemble-pages.sh <coverage-report-dir> <mutation-report-dir> <output-dir>}"

# The sentinel is what makes the rm below safe. It is written into the output
# directory as the first act of a run, so this script will only ever delete a
# directory it can prove it created itself.
sentinel_name=".assembled-by-anichron"

die() { printf 'assemble-pages: %s\n' "$1" >&2; exit 1; }

[ -d "$coverage_dir" ] || die "coverage report directory not found: $coverage_dir"
[ -f "$coverage_dir/index.html" ] || die "no index.html in coverage report directory: $coverage_dir"

# ---------------------------------------------------------------------------
# Output guard. Read this before changing anything below it.
#
# An earlier version of this script ran `rm -rf "$output"` with nothing but the
# input checks above in front of it. On 2026-09-21 a test written to prove it
# refused dangerous paths was run before this guard existed, passed $HOME, and
# deleted a home directory. BSD rm self-protects `/`, `.` and `..`; nothing
# protects $HOME.
#
# The lesson taken is not "add more forbidden paths" -- a blocklist is only ever
# as good as its author's imagination, and the path that did the damage was
# supplied by the caller at runtime. Instead the destructive step is made
# self-limiting: this script removes a directory ONLY if that directory contains
# the sentinel file it writes itself. A directory it did not create is refused,
# not deleted. The blocklist below is kept as a cheap second layer for the cases
# where refusing early gives a clearer message than the sentinel check would.
# ---------------------------------------------------------------------------

# Resolve to an absolute path first, so `pages-site/../..` cannot smuggle the
# target upward past the checks that follow.
output_parent=$(cd "$(dirname "$output")" 2>/dev/null && pwd) || die "output parent directory does not exist: $(dirname "$output")"
output_abs="$output_parent/$(basename "$output")"

# Collapse repeated slashes and drop a trailing one, so the comparisons below
# see a canonical path. Without this, `/` arrives as `///` -- dirname and
# basename both yield `/` -- and slips past the blocklist to be caught by the
# sentinel check instead, with a message about a missing sentinel rather than
# about the filesystem root. Caught by this script's own test suite.
while [ "$output_abs" != "${output_abs//\/\//\/}" ]; do output_abs="${output_abs//\/\//\/}"; done
case "$output_abs" in
  /) ;;
  */) output_abs="${output_abs%/}" ;;
esac

case "$(basename "$output")" in
  .|..) die "refusing to use a relative directory reference as output: $output" ;;
esac

home_abs=$(cd "$HOME" 2>/dev/null && pwd || echo "$HOME")
for forbidden in "/" "$home_abs" "$output_parent/.." ; do
  [ "$output_abs" = "$forbidden" ] && die "refusing to write the site to $output_abs"
done

# The working directory and anything above it. `rm -rf` on the ground you are
# standing on is never in scope, however the output path was spelled.
cwd_abs=$(pwd)
case "$cwd_abs/" in
  "$output_abs"/*) die "refusing an output directory that contains the working directory: $output_abs" ;;
esac

# An input must not live inside the output, or preparing the output destroys the
# thing being copied. This is the case that made the earlier version dangerous
# in ordinary use: the three input checks passed, then `rm -rf coverage` removed
# the report before `cp` could read it.
for input in "$coverage_dir" "$mutation_dir"; do
  [ -d "$input" ] || continue
  input_abs=$(cd "$input" && pwd)
  [ "$input_abs" = "$output_abs" ] && die "output directory is also an input: $input_abs"
  case "$input_abs/" in
    "$output_abs"/*) die "refusing an output directory that contains the input $input_abs" ;;
  esac
done

# The sentinel check. This is the guard that actually bounds the delete.
if [ -e "$output_abs" ]; then
  [ -d "$output_abs" ] || die "output path exists and is not a directory: $output_abs"
  if [ ! -f "$output_abs/$sentinel_name" ]; then
    die "refusing to delete $output_abs: no $sentinel_name, so this script did not create it"
  fi
  rm -rf "$output_abs"
fi

mkdir -p "$output_abs"
# Written before anything is copied, so an interrupted run still leaves a
# directory the next run recognises as its own rather than refusing to touch.
printf 'Generated by scripts/assemble-pages.sh. Safe to delete.\n' > "$output_abs/$sentinel_name"

# ---------------------------------------------------------------------------
# Assemble
# ---------------------------------------------------------------------------

# `cp -R "$dir/."` rather than `cp -R "$dir"`: the trailing /. copies the
# contents into an existing destination, and behaves identically on BSD and GNU
# cp. It carries dotfiles, dot-directories and nested directories.
mkdir -p "$output_abs/coverage"
cp -R "$coverage_dir/." "$output_abs/coverage/"

mutation_published=false
if [ -d "$mutation_dir" ] && [ -f "$mutation_dir/mutation-report.html" ]; then
  mkdir -p "$output_abs/mutation"
  cp -R "$mutation_dir/." "$output_abs/mutation/"
  mutation_published=true
fi

# Pages runs Jekyll by default, which drops files and directories whose names
# begin with an underscore -- both report generators emit some.
touch "$output_abs/.nojekyll"

if [ "$mutation_published" = true ]; then
  mutation_card='    <a class="card" href="mutation/mutation-report.html">
      <h2>Mutation report</h2>
      <p>Which mutants survived, in source context. A surviving mutant is a line
         the tests execute but do not actually check.</p>
    </a>'
else
  mutation_card='    <div class="card missing">
      <h2>Mutation report</h2>
      <p>Not available for this build. The mutation sweep produced no HTML
         report, which means it failed rather than scored badly &mdash; see the
         job summary and the <code>mutation-report</code> artifact.</p>
    </div>'
fi

cat > "$output_abs/index.html" <<HTML
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Anichron &middot; quality reports</title>
  <style>
    :root { color-scheme: light dark; }
    body {
      font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
      line-height: 1.5; margin: 0 auto; padding: 2.5rem 1.25rem; max-width: 46rem;
    }
    h1 { font-size: 1.5rem; margin: 0 0 .25rem; }
    .sub { opacity: .7; margin: 0 0 2rem; }
    .card {
      display: block; padding: 1.1rem 1.25rem; margin-bottom: 1rem;
      border: 1px solid currentColor; border-radius: .5rem;
      text-decoration: none; color: inherit;
    }
    .card h2 { font-size: 1.1rem; margin: 0 0 .35rem; }
    .card p { margin: 0; opacity: .8; font-size: .95rem; }
    .card.missing { opacity: .55; border-style: dashed; }
    footer { margin-top: 2.5rem; font-size: .85rem; opacity: .6; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
  </style>
</head>
<body>
  <h1>Anichron quality reports</h1>
  <p class="sub">Published from the latest default-branch build.</p>
  <main>
    <a class="card" href="coverage/index.html">
      <h2>Coverage report</h2>
      <p>Line and branch coverage per project, down to the statement.</p>
    </a>
$mutation_card
  </main>
  <footer>
    Generated by <code>scripts/assemble-pages.sh</code>.
    Mutation testing is documented in <code>docs/mutation-testing.md</code>.
  </footer>
</body>
</html>
HTML

printf 'assembled %s (coverage: yes, mutation: %s)\n' \
  "$output_abs" "$([ "$mutation_published" = true ] && echo yes || echo no)"
