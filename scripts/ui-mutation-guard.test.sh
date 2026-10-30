#!/usr/bin/env bash
# Tests for ui-mutation-guard.sh. Run: scripts/ui-mutation-guard.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/ui-mutation-guard.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/ui-mutation-guard-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

# A minimal repository that satisfies every rule.
tree() {
  rm -rf "$work/repo"
  local ui="$work/repo/src/Anichron.UI"
  mkdir -p "$ui/src/app" "$work/repo/docs/adr" "$work/repo/.github/workflows" "$work/repo/scripts"
  cat >"$ui/stryker.config.json" <<'JSON'
{ "testRunner": "vitest", "mutate": ["src/**/*.ts", "src/app/router.tsx", "!src/**/*.test.ts"],
  "thresholds": { "high": 100, "low": 100, "break": 100 } }
JSON
  printf '{ "scripts": { "mutate": "stryker run" } }\n' >"$ui/package.json"
  printf '| `src/app/router.tsx` | back |\n| `!src/**/*.test.ts` | tests |\n| `src/app/clock.ts` | equivalent |\n' \
    >"$work/repo/docs/adr/0014-ui.md"
  printf '// Stryker disable next-line all\nexport const tick = 1;\n' >"$ui/src/app/clock.ts"
  printf 'jobs:\n  ui:\n    steps:\n      - run: scripts/ui-mutation-changed.sh --base origin/master\n' \
    >"$work/repo/.github/workflows/ci.yml"
  printf '#!/usr/bin/env bash\nargs=(--mutate "$files")\npnpm exec stryker run --mutate "$files"\n' \
    >"$work/repo/scripts/ui-mutation-changed.sh"
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

echo 'ui-mutation-guard.sh'

tree
check 'a conforming setup passes, including the sanctioned --mutate' 0 'matches its rules'

tree
sed -i.bak 's/"vitest"/"command"/' "$work/repo/src/Anichron.UI/stryker.config.json"
check 'a runner other than vitest fails' 1 'testRunner is "command"'

tree
sed -i.bak 's/"break": 100/"break": 90/' "$work/repo/src/Anichron.UI/stryker.config.json"
check 'a threshold below 100 fails' 1 'thresholds.break is 90'

tree
sed -i.bak 's/"testRunner"/"incremental": true, "testRunner"/' "$work/repo/src/Anichron.UI/stryker.config.json"
check 'incremental mode fails' 1 'incremental is set'

tree
sed -i.bak 's/"testRunner"/"ignoreStatic": true, "testRunner"/' "$work/repo/src/Anichron.UI/stryker.config.json"
check 'ignoreStatic fails' 1 'ignoreStatic is set'

tree
sed -i.bak 's#"src/\*\*/\*.ts", ##' "$work/repo/src/Anichron.UI/stryker.config.json"
check 'a mutate list that does not start from the logic files fails' 1 'must start with "src/**/*.ts"'

tree
sed -i.bak 's#"!src/\*\*/\*.test.ts"#"!src/**/*.test.ts", "!src/shared/**"#' "$work/repo/src/Anichron.UI/stryker.config.json"
check 'an exclusion the ADR does not name fails' 1 'mutate entry !src/shared/** is not named in ADR 0014'

tree
sed -i.bak 's#"src/app/router.tsx"#"src/app/Page.tsx"#' "$work/repo/src/Anichron.UI/stryker.config.json"
check 'a view brought back into scope that the ADR does not name fails' 1 'mutate entry src/app/Page.tsx'

tree
printf '      - run: pnpm exec stryker run --incremental\n' >>"$work/repo/.github/workflows/ci.yml"
check 'a measurement flag in a workflow fails' 1 '.github/workflows/ci.yml: passes a StrykerJS flag'

tree
printf '{ "scripts": { "mutate": "stryker run --ignoreStatic" } }\n' >"$work/repo/src/Anichron.UI/package.json"
check 'a measurement flag in a package script fails' 1 'src/Anichron.UI/package.json: passes a StrykerJS flag'

tree
printf '#!/usr/bin/env bash\nstryker run -m src/a.ts\n' >"$work/repo/scripts/other.sh"
check '--mutate outside ui-mutation-changed.sh fails' 1 'scripts/other.sh: passes --mutate'

tree
printf '      - run: cd src/Anichron.UI && pnpm mutate\n' >>"$work/repo/.github/workflows/ci.yml"
check 'a workflow running StrykerJS directly fails' 1 'runs StrykerJS directly'

tree
printf '      - run: pnpm exec stryker run\n' >>"$work/repo/.github/workflows/ci.yml"
check 'a workflow calling stryker run fails' 1 'runs StrykerJS directly'

tree
printf '// Stryker disable next-line all\nexport const x = 1;\n' >"$work/repo/src/Anichron.UI/src/app/other.ts"
check 'a directive in a file the ADR does not name fails' 1 'src/Anichron.UI/src/app/other.ts: has a Stryker directive'

tree
printf '{ "scripts": { "m": "stryker run -m src/a.ts" } }\n' >"$work/repo/src/Anichron.UI/package.json"
check '--mutate at the end of a package script fails' 1 'package.json: passes --mutate'

tree
rm "$work/repo/docs/adr/0014-ui.md"
check 'a missing ADR exits 2' 2 'cannot read'

tree
echo 'not json' >"$work/repo/src/Anichron.UI/stryker.config.json"
check 'an unreadable config exits 2' 2 'cannot read'

if [ "$failures" -gt 0 ]; then
  echo "$failures failure(s)"
  exit 1
fi
echo 'all passed'
