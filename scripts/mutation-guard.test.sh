#!/usr/bin/env bash
# Tests for mutation-guard.sh. Run: scripts/mutation-guard.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/mutation-guard.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/mutation-guard-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

# A minimal repository that satisfies every rule.
tree() {
  rm -rf "$work/repo"
  mkdir -p "$work/repo/src/App" "$work/repo/docs/adr" "$work/repo/.github/workflows" "$work/repo/scripts"
  cat >"$work/repo/src/stryker-config.json" <<'EOF'
{ "stryker-config": { "test-runner": "mtp", "mutate": ["!**/Migrations/**", "!**/App/Wiring.cs"],
  "thresholds": { "high": 100, "low": 100, "break": 100 } } }
EOF
  printf '| `Migrations/**` | 1 |\n| `App/Wiring.cs` | 2 |\n| `Hasher` | clears a buffer |\n' >"$work/repo/docs/adr/0005-scope.md"
  printf 'namespace App;\n\npublic class Hasher\n{\n    // Stryker disable once Statement\n}\n' >"$work/repo/src/App/Hasher.cs"
  printf 'jobs:\n  m:\n    steps:\n      - run: scripts/stryker-sweep.sh --output ../mutation\n' >"$work/repo/.github/workflows/ci.yml"
  printf 'jobs:\n  d:\n    steps:\n      - run: scripts/stryker-sweep.sh --concurrency 1\n' >"$work/repo/.github/workflows/mutation-divergence.yml"
  printf '#!/usr/bin/env bash\nargs+=(-m "**/x.cs")\ndotnet stryker "${args[@]}" --mutate "**/y.cs"\n' >"$work/repo/scripts/mutation-changed.sh"
}

check() {
  local label="$1" want_status="$2" needle="$3" out status
  out=$("$script" "$work/repo" 2>&1); status=$?
  if [ "$status" -ne "$want_status" ]; then
    fail "$label" "exit $want_status" "exit $status with: $out"
  elif [[ "$out" != *"$needle"* ]]; then
    fail "$label" "output containing '$needle'" "$out"
  else
    pass "$label"
  fi
}

echo 'mutation-guard.sh'

tree
check 'a conforming setup passes, including the two sanctioned flags' 0 'matches its rules'

tree
sed -i.bak 's/"mtp"/"vstest"/' "$work/repo/src/stryker-config.json"
check 'a runner other than mtp fails' 1 'test-runner is "vstest"'

tree
sed -i.bak 's/"break": 100/"break": 99/' "$work/repo/src/stryker-config.json"
check 'a threshold below 100 fails' 1 'thresholds.break is 99'

tree
printf '      - run: scripts/stryker-sweep.sh --since master\n' >>"$work/repo/.github/workflows/ci.yml"
check '--since fails' 1 'ci.yml: passes --since or --with-baseline'

tree
printf '      - run: scripts/stryker-sweep.sh --with-baseline:master\n' >>"$work/repo/.github/workflows/ci.yml"
check '--with-baseline fails' 1 'passes --since or --with-baseline'

tree
printf '      - run: scripts/stryker-sweep.sh --break-at 90\n' >>"$work/repo/.github/workflows/ci.yml"
check 'a threshold flag fails' 1 'passes a threshold flag'

tree
printf '      - run: scripts/stryker-sweep.sh --concurrency 2\n' >>"$work/repo/.github/workflows/ci.yml"
check '--concurrency outside the divergence job fails' 1 '.github/workflows/ci.yml: passes --concurrency'

tree
printf '      - run: scripts/stryker-sweep.sh -m "**/z.cs"\n' >>"$work/repo/.github/workflows/ci.yml"
check '-m outside mutation-changed.sh fails' 1 '.github/workflows/ci.yml: passes --mutate'

tree
printf '      - run: dotnet stryker --output ../other\n' >>"$work/repo/.github/workflows/mutation-divergence.yml"
check 'a workflow running dotnet stryker directly fails' 1 \
  '.github/workflows/mutation-divergence.yml: runs dotnet stryker directly'

tree
printf 'namespace App;\n\npublic class Unlisted\n{\n    // Stryker disable once String\n}\n' >"$work/repo/src/App/Unlisted.cs"
check 'a directive in a file ADR 0005 does not name fails' 1 'src/App/Unlisted.cs: has a Stryker directive'

tree
sed -i.bak 's#"!\*\*/App/Wiring.cs"#"!**/App/Wiring.cs", "!**/App/Other.cs"#' "$work/repo/src/stryker-config.json"
check 'an excluded path ADR 0005 does not name fails' 1 'excludes App/Other.cs'

tree
rm "$work/repo/docs/adr/0005-scope.md"
check 'a missing ADR is a usage error' 2 'cannot read'

if [ "$failures" -gt 0 ]; then
  printf '\n%d failure(s)\n' "$failures"
  exit 1
fi
printf '\nall passed\n'
