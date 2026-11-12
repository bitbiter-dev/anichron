#!/usr/bin/env bash
# Tests for tested-on-pr.sh. Run: scripts/tested-on-pr.test.sh
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/tested-on-pr.sh"
failures=0
work=$(mktemp -d "${TMPDIR:-/tmp}/tested-on-pr-test.XXXXXXXX")
trap 'rm -rf "$work"' EXIT

pass() { printf '  ok   %s\n' "$1"; }
fail() { printf '  FAIL %s\n     expected: %s\n     actual:   %s\n' "$1" "$2" "$3"; failures=$((failures + 1)); }

mkdir -p "$work/bin" "$work/state"
cat >"$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
case "$2" in
  repos/{owner}/{repo}/commits/*/pulls) cat "$GH_STATE/pulls" ;;
  repos/{owner}/{repo}/git/commits/*) cat "$GH_STATE/head" ;;
  repos/{owner}/{repo}/commits/*/check-runs*) cat "$GH_STATE/checks" ;;
  *) exit 9 ;;
esac
EOF
chmod +x "$work/bin/gh"

git_as_mira() { git -c user.name=mira -c user.email=mira@example.org "$@"; }
git init -q "$work/repo"
git_as_mira -C "$work/repo" commit -q --allow-empty -m base
sha=$(git -C "$work/repo" rev-parse HEAD)
tree=$(git -C "$work/repo" rev-parse 'HEAD^{tree}')

# state <merge-commit-sha> <head-tree> <gate-conclusions...> (oldest first)
state() {
  local merge="$1" head_tree="$2"
  shift 2
  printf '[{"number":3,"merge_commit_sha":"other","head":{"sha":"x"}},{"number":7,"merge_commit_sha":"%s","head":{"sha":"h7"}}]\n' "$merge" >"$work/state/pulls"
  printf '{"tree":{"sha":"%s"}}\n' "$head_tree" >"$work/state/head"
  local i=0 c runs=''
  for c in "$@"; do
    i=$((i + 1))
    runs="$runs${runs:+,}{\"status\":\"completed\",\"conclusion\":\"$c\",\"completed_at\":\"2026-11-13T10:0$i:00Z\"}"
  done
  printf '{"check_runs":[%s{"status":"in_progress","conclusion":null,"completed_at":null}]}\n' "${runs:+$runs,}" >"$work/state/checks"
}

run() { (cd "$work/repo" && GH_STATE="$work/state" PATH="$work/bin:$PATH" "$script" "${1:-$sha}" 2>"$work/err"); }

expect() {
  local label="$1" want="$2" reason="$3" out
  out=$(run "${4:-$sha}")
  if [ "$out" = "$want" ] && grep -qF -- "$reason" "$work/err"; then pass "$label"; else fail "$label" "$want, $reason" "$out, $(cat "$work/err")"; fi
}

echo 'tested-on-pr.sh'

state "$sha" "$tree" failure success
expect 'a merge of a head with this tree that passed CI gate is tested' 'tested=true' "#7's head h7 has this tree"

state "$sha" "$tree" success failure
expect 'the latest completed CI gate decides' 'tested=false' "last CI gate on h7 is 'failure'"

state "$sha" "$tree"
expect 'a head with no CI gate is untested' 'tested=false' "is 'missing'"

state "$sha" other-tree success
expect 'a head with another tree is untested' 'tested=false' 'has another tree'

state not-this success
expect 'a commit no pull request merged is untested' 'tested=false' 'is not the merge commit of a pull request'

rm "$work/state/pulls"
expect 'a failing lookup is untested' 'tested=false' 'cannot list'

expect 'an unknown commit is untested' 'tested=false' 'cannot read the tree' 0000000000000000000000000000000000000000

if [ "$failures" -gt 0 ]; then
  printf '\n%d failure(s)\n' "$failures"
  exit 1
fi
printf '\nall passed\n'
