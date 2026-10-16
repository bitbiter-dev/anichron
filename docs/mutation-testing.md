# Mutation testing

Line coverage tells you which code the tests *execute*. Mutation testing tells you whether the
assertions would **notice if that code were wrong**. Stryker.NET rewrites the production code in
small, deliberately wrong ways — a `<` becomes `<=`, a `&&` becomes `||`, a statement is removed —
and re-runs the suite against each mutant. A mutant no test fails on is a **survivor**: a real
behaviour change the suite is blind to.

## Running it

From `src/` — not the repository root:

```bash
dotnet tool restore
cd src
dotnet stryker
```

**The working directory matters.** Stryker resolves projects relative to the current directory, so
running it from the repository root fails with `No .csproj or .fsproj file found` even though the
solution file parses correctly.

No flags are needed locally and no **measurement-affecting** flag should be added. Every value that
affects what gets measured lives in `src/stryker-config.json`, which is what makes a local run and
the CI run comparable. The one local exception is the scoped run below, which deliberately measures
less and is never compared with CI.

CI does pass threshold flags — `--break-at`, `--threshold-low` and `--threshold-high`, computed
from the stored high-water mark (see [The ratchet](#the-ratchet)). Those change the **verdict**,
not the measurement, so the two runs remain comparable. Running `dotnet stryker` with no flags
locally therefore measures the same thing CI does, but judges it against the committed floor rather
than the current mark.

> **Partly wired into CI.** The gate runs on every pull request and fails below the `break`
> threshold (#180). The first hosted run has now been observed: it scored **46.58%**, the same
> score the `42` threshold was calibrated against on a developer machine, so the headroom is
> measured on both and not just locally
> ([run 35577766467](https://github.com/bitbiter-dev/anichron/actions/runs/35577766467)). The
> badge (#182), the published report (#181), the weekly divergence check (#184) and the threshold
> ratchet (#183) are all live.

A full sweep takes about eight minutes on a developer machine, with live progress as it goes. It finishes with the kill summary and the score; open the HTML report it prints the path
to for the detail — it shows each surviving mutant in its source context, which is how you find the
line that needs a test. The markdown report beside it has the per-file breakdown.

### The scoped local run

A full sweep now takes about eight minutes, and the CI `Mutation` job runs one on every pull
request anyway. While working on a change, run only the files it touches:

```bash
scripts/mutation-changed.sh                     # changed since the merge-base with origin/master
scripts/mutation-changed.sh --base <ref>        # a different base
scripts/mutation-changed.sh --print             # show the Stryker arguments, run nothing
scripts/mutation-changed.sh Anichron.API/Endpoints/AdminUserEndpoints.cs   # add source files
```

It collects every changed `.cs` file under `src/` — committed, staged, unstaged and untracked —
and passes each one to Stryker as `--mutate`, together with the exclusions from
`stryker-config.json`. Mutants outside those files are reported as `Removed by mutate filter`, so
the score and the `break` threshold apply to the changed files alone. Stryker still builds the
solution, creates every mutant and captures per-test coverage for the whole suite, which is the
fixed cost of roughly a minute and a half. The run then tests only the selected mutants, and each
of them against every test that covers it, so a file's result matches what the full sweep reports
for that file.

A changed test file `<Name>Tests.cs` selects `<Name>.cs` in the matching production project. About
a fifth of the test files are not named after one source file (endpoint and registration tests,
test doubles), and a change to one of those exits `1` and lists them, because silently mutating
nothing would read as a pass. Pass the source files they cover as arguments, relative to `src/`.

What the scoped run cannot see, and the full sweep in CI catches:

- a change in one file that weakens the tests of a file it did not touch
- a change to `stryker-config.json`, a shared base class or a test double, whose effect is not
  confined to the files in the diff — run `dotnet stryker` for those

**Concurrent local runs corrupt each other unless each has its own temp directory.** The MTP
runner hands the active mutant to the test hosts through a memory-mapped file at a fixed path,
`$TMPDIR/stryker-mutant-<n>.txt`, where `<n>` is only the runner index (0 to `concurrency − 1`).
Two Stryker runs on one machine — two agents in two worktrees — overwrite each other's mutant IDs,
so tests run against the wrong mutant and killable mutants are reported as survivors. Measured on
the same 138 mutants: 137 killed and 100% alone; 98.55% and 94.93% for two simultaneous runs; 100%
for both again when each had its own `TMPDIR`. Earlier symptoms were runs that swung between 89 and
174 survivors on unchanged code, and one at 0.00%. `mutation-changed.sh` therefore runs Stryker
with a private `TMPDIR` that it removes afterwards; run a full sweep the same way (see CLAUDE.md).
CI is unaffected: each job runs alone on its own runner.

**Why not Stryker's own `--since`.** It is the obvious tool and it is unusable here: Stryker
resolves the repository through its main working directory, so inside a git worktree — where every
agent works — it diffs the main checkout against the target instead. Measured on a worktree with an
untested method: it listed the main checkout's changed files, tested 0 mutants, printed "unable to
calculate a mutation score" and **exited `0`**. `--with-baseline` builds on the same diff.

## Reading the result

```
Killed:   447     a test failed — the mutant was detected
Survived: 207     no test failed — a real gap
Timeout:    3     the mutant hung the tests; counts as detected
```

Uncovered mutants (`NoCoverage`) count against the score too, so the score is
`detected / (detected + survived + uncovered)`.

Stryker exits `2` when the score is below the `break` threshold in the config — not `1`, despite
what its documentation says — so CI treats any non-zero value as failure rather than matching on a
specific code.

In CI the sweep runs in its own `Mutation` job, in parallel with `Checks`, with
`continue-on-error` on the sweep and the job's **last** step failing on the outcome. Three
constraints force that shape, and changing any part of it tends to break one of the others:

- GitHub applies an implicit `success()` to any `if:` without a status-check function. A step that
  aborted mid-job would therefore silently skip the summary, the report upload and the badge and
  high-water payloads — so enforcement has to be deferred to the end.
- The `Mutation` job is not itself a required check. It blocks a merge through `CI gate`, the one
  required check this workflow reports, which fails unless `Checks`, `Mutation` and the pull-request
  Docker builds all succeeded (`scripts/ci-gate.sh`).
- A job-level `if:` does **not** relax a `needs:` success requirement — only `always()` or
  `!cancelled()` does, and the implicit `success()` looks at *every* upstream job, not only the
  direct ones. So `publish` uses `!cancelled()` plus an explicit `needs.checks.result`, and
  `deploy-pages` uses `!cancelled()` guarded on the Pages artifact actually having been uploaded.
  That keeps the reports publishing when the gate fails, while still skipping cleanly when an
  earlier failure meant no artifact was produced.

A mutation regression therefore fails `CI gate` and blocks the merge, without suppressing the
coverage artifact, the badge or the Pages deployment. Image pushes do get skipped: they run only
after `CI gate` succeeds.

## Configuration

| Setting | Value | Why |
| --- | --- | --- |
| `test-runner` | `mtp` | **Required.** See [ADR 0001](adr/0001-mutation-testing-on-the-mtp-runner.md). |
| `mutate` | excludes `Migrations/**` and a short, explicit list of pure-wiring files | See [ADR 0005](adr/0005-mutation-scope-excludes-unkillable-and-wiring-code.md). Paths are listed one by one — never a wildcard. |
| `ignore-methods` | `Log.*`, `*BeginScope`, `UnreachableException.ctor` | Calls whose mutants no test can meaningfully kill. See [ADR 0005](adr/0005-mutation-scope-excludes-unkillable-and-wiring-code.md). |
| `thresholds` | `break`, `low` and `high` all `100` | The target since ADR 0005, reached with #233. All three move together because Stryker validates `break <= low <= high` before it mutates anything. They are also the **floor** the ratchet derives its gate from, so a run with no stored mark gates at 100 as well — see [The ratchet](#the-ratchet). |
| `concurrency` | *unset* | Left at Stryker's default (half the logical processors). Pinning it above the default oversubscribes CI, and timeouts count as *detected*, so that inflates the score. |

> **Do not change `test-runner` back to Stryker's default (`vstest`).** It cannot observe xUnit v3
> test failures at all and produces a silent `0.00%` with no error — which reads as a catastrophic
> test suite rather than a broken measurement. The reasons are in ADR 0001.

## When to re-measure the baseline

The MTP runner carries an open upstream defect that can silently under-report kills. Why that
matters, and why a version pin doesn't guard it, is in
[ADR 0001](adr/0001-mutation-testing-on-the-mtp-runner.md#consequences) — the short version is that
it is test-shape-dependent, so a change to the test suite can introduce it with no dependency
change at all.

Re-run the sweep single-threaded and compare the **detected count** (not the score, not the total)
against a normal run after:

- upgrading Stryker, or
- a significant change to the shape of the test suite.

```bash
cd src
dotnet stryker --concurrency 1
```

Note that Stryker's own documented cross-check — re-running with coverage analysis disabled — does
**not** detect this defect. Comparing detected counts across concurrency levels is what does.

Also: scoping a run to a single file is reported upstream to lose kills even single-threaded, so
bisecting a score change file-by-file is unreliable. Read the HTML report instead.

## Post-processing the report

`scripts/mutation-report.sh` derives the two things CI needs from a JSON report. It needs only
bash and `jq`, and runs anywhere with no CI context.

```bash
# shields.io endpoint payload for the badge
scripts/mutation-report.sh badge mutation/reports/mutation-report.json

# compare two runs' detected counts; non-zero exit means they diverged
scripts/mutation-report.sh compare run-a.json run-b.json
```

The badge's colour bands come from the **report's own** `thresholds` object, which Stryker copies
from `stryker-config.json` — so the badge cannot drift from the configured gate. At or above
`high` is `brightgreen`, at or above `low` is `yellow`, below that is `red`. Both keys must be
present: jq evaluates `x >= null` as true, so a partial `thresholds` object would otherwise
silently colour a failing score green.

`compare` deliberately compares **detected counts**, not scores. The upstream defect it guards
keeps killed-plus-survived constant while moving the split between them, so a single run looks
internally consistent and a score comparison can miss it.

Both subcommands distinguish a **bad measurement** from a **bad score**, and fail loudly on the
first. A missing file, malformed JSON, a report whose `thresholds` are absent or partial, or a
report with no *detectable* mutants at all is an error — never a number.

A detected count of zero, on the other hand, is a legitimate measurement of a suite that killed
nothing, and does report `0%`. The distinction matters most for `compare`: two runs that each
measured nothing would otherwise "agree" and pass the divergence gate, which is the likeliest way
a collapsed measurement could look healthy. Both are refused instead.

A report containing `Pending` mutants is also refused. Pending means the run never finished, and
since those mutants are absent from the denominator the resulting score would read *better* than
reality — the one direction of error a quality gate must never make.

That whole guard exists because a silent zero looks like a catastrophic test suite rather than a
broken measurement — the same failure shape as the VSTest runner in
[ADR 0001](adr/0001-mutation-testing-on-the-mtp-runner.md).

## Publishing the badge

`scripts/publish-badges.sh` writes the coverage and mutation badges onto the orphan `badges`
branch, which the README reads from. CI runs it on default-branch builds only, so a pull request
cannot move a badge.

```bash
MUTATION_BADGE=/path/to/mutation.json scripts/publish-badges.sh coverage/report/badge_linecoverage.svg
```

It **adds** to the branch rather than rebuilding it. The earlier inline version cleared the branch
on every run, which was harmless while `coverage.svg` was the only artifact but would have deleted
the mutation badge and the ratchet's stored high-water mark.

The payload is **validated, not just existence-checked**: an absent, empty, truncated, or
non-conforming `MUTATION_BADGE` leaves any existing `mutation.json` untouched. That matters because
a generator which fails *after* its output has been redirected leaves a zero-byte file behind, and
force-pushing that would destroy a working badge and render as `invalid`. Keeping the last good
badge always beats overwriting it with junk.

Because it force-pushes, it also refuses to guess about the remote. `git ls-remote` distinguishes
"the branch does not exist yet" from "I could not reach origin": the first creates the branch, the
second aborts. Treating an unreachable origin as a missing branch would recreate `badges` from
scratch and wipe every artifact on it. The fetch is forcing, so a local `badges` left over from an
earlier run cannot diverge and then be pushed over the top of the remote.

Note the ordering constraint in CI: the badge payload is generated **before** this script runs,
because checking out `badges` removes `scripts/` from the work tree. The `Mutation` job writes the
payload and any new high-water mark, and hands them to the `publish` job as the `mutation-state`
artifact, which is downloaded into `$RUNNER_TEMP`, outside the repository entirely. A file missing
from that artifact means "nothing new", exactly as an absent path does.

## Publishing the report

`scripts/assemble-pages.sh` builds the Pages site from both reports:

```
pages-site/
  index.html                landing page linking whichever reports are present
  coverage/                 the coverage report
  mutation/                 the mutation report
  .nojekyll                 so Pages serves paths beginning with an underscore
  .assembled-by-anichron    the output guard's sentinel — see below
```

`.assembled-by-anichron` is published along with everything else, because
`upload-pages-artifact` takes the whole directory. That is accepted rather than worked around: the
sentinel has to live in the output directory to mark it, and deleting it after a successful run
would make the next run refuse to rebuild. A 60-byte text file at the site root is a cheap price
for a delete that cannot escape.

The coverage report moved out of the site root into `coverage/`, so **the coverage URL changed** to
`https://bitbiter-dev.github.io/anichron/coverage/`. That was #181's call: serving two reports means
subdirectories, and a root that arbitrarily served one of them was accidental rather than designed.
The README badges point at the new locations.

It runs on default-branch builds only, so a pull request still gets the report as a CI artifact and
the score in the job summary, never a URL. User stories 8 and 9 read as though contributors get a
URL on every PR; they do not.

### Why the mutation report is optional, and the earlier rule that got reversed

If Stryker crashes it produces no HTML report. The script publishes coverage alone and the landing
page says the mutation report is unavailable *for this build*, naming the distinction that matters:
the sweep **failed** rather than scored badly.

An earlier draft of this script enforced "both reports or neither" and failed when either was
missing. That was wrong in two ways. It would fail CI a second time for a reason that is only
about publishing, when the mutation gate had already reported the real
problem. And it would publish *nothing* — losing the coverage report, which used to ship regardless
of the mutation outcome. A missing report is honest; a stale one is misleading; neither is worth
blocking a merge over.

### Why it runs before the badge step

`.github/workflows/ci.yml` assembles the site **before** `Update quality badges`, and the order is
load-bearing. `publish-badges.sh` switches the work tree to the orphan `badges` branch and restores
it in an `EXIT` trap that swallows its own failure and still exits 0 — so a failed restore leaves
every later step running on a branch where neither `scripts/` nor `coverage/` exists. Assembling
first means a restore failure can cost the upload but not the assembly.

`pages-site/` is gitignored on purpose. `publish-badges.sh` manipulates the work tree with
`git checkout --force`, `git checkout --orphan`, `git rm -rf .`, `git reset` and a plain
`git checkout` to restore — and crucially **no `git clean`**, which is the one that would take an
ignored directory with it. So the assembled site survives the branch switch. Verified by reading
that script; a `git clean -fdx` added to it later would silently break this.

### The output guard, and why it is a sentinel rather than a blocklist

The script has to clear its output directory before rebuilding it. An earlier version did that with
an unguarded `rm -rf "$output"`, and on 2026-09-21 a test written to prove it *refused* dangerous
paths was run before that guard existed, passed `$HOME`, and deleted a home directory. BSD `rm`
self-protects `/`, `.` and `..`; nothing protects `$HOME`.

The fix is not a longer list of forbidden paths — a blocklist is only as good as its author's
imagination, and the path that did the damage was supplied by the caller at runtime. Instead the
delete is made self-limiting: the script writes a sentinel file (`.assembled-by-anichron`) into the
directory as its first act, and **removes a directory only if that sentinel is present.** A
directory it did not create is refused, not deleted. The cheap checks for `/`, `$HOME`, `.`, `..`,
and for an output that contains one of its own inputs, are kept as a second layer that gives a
clearer message.

⚠️ The corollary for anyone extending `scripts/assemble-pages.test.sh`: every dangerous path in that
file is a **fake** built inside a temp directory, and a fake `$HOME` is injected with
`env HOME=...`. A test that needs the guard to work in order to be safe is a test that can only be
run once.

### Verifying the scripts

```bash
scripts/mutation-report.test.sh
scripts/publish-badges.test.sh
scripts/assemble-pages.test.sh
scripts/mutation-ratchet.test.sh
```

`mutation-report.test.sh` covers `badge`, `compare` and `tally`. The `tally` cases matter to the
divergence job specifically. `tally` counts through the shared `TALLY` expression and then computes
the percentage, and `badge` is built **on top of** `tally` rather than beside it — so the job
summary cannot disagree with the badge about either number, and a run with pending mutants is
refused on that path too rather than reporting a score that reads better than reality.

`mutation-ratchet.test.sh` covers the threshold arithmetic against fixtures rather than against a
real sweep, because the cases worth pinning are the ones a real sweep will not reach for months: a
mark whose derived gate falls below the committed floor, and a mark high enough that the derived
`low` would overtake `threshold-high`. Both are asserted as *values*, and the `break <= low <= high`
chain is asserted directly, since that inequality is what Stryker rejects before it mutates
anything.

All four run in CI, in the `Checks` job — so they are skipped, not run, if the build, tests,
coverage or formatting steps have already failed.

`publish-badges.test.sh` executes real `git push` commands, but the `Checks` job's checkout holds
only a read token; push rights exist only in the master-only `publish` job. The test script still
guards itself twice — every setup step is checked, and it refuses to run at all unless `origin`
resolves inside its own temp directory.

One consequence worth knowing: because the `publish` job needs `Checks` to succeed, a failing
helper test stops the coverage badge and the Pages upload too. That is intended for the badge — a
publisher whose tests fail should not publish — but it does mean the Pages site can go stale on a
failure unrelated to coverage.

`publish-badges.test.sh` builds a throwaway repository with a bare remote and exercises the real
push path. The cases that matter are the destructive ones: an artifact the script knows nothing
about survives an update; an empty or truncated payload does not overwrite a good badge; a *diverged*
local branch does not clobber the remote; an unreachable origin is refused rather than treated as a
missing branch; and a detached HEAD is restored rather than left on `badges`.

`mutation-report.test.sh` is fixture-driven, no network, about a second. The fixtures in
`scripts/fixtures/` are deliberately
tiny and hand-written rather than real reports — a real one embeds the full source of every mutated
file and runs to megabytes. `divergent-a.json` and `divergent-b.json` both hold ten mutants with
`Killed + Survived = 10` and differ only in the split (8/2 versus 5/5), reproducing the upstream
defect's signature so the comparison is tested against the thing it exists to catch.

## The ratchet

The gate is not the number in `stryker-config.json`. It is derived on every run from a **high-water
mark** stored on the `badges` branch as `mutation-highwater.json`, by `scripts/mutation-ratchet.sh`:

```bash
# what CI passes to Stryker, given a stored mark
scripts/mutation-ratchet.sh thresholds src/stryker-config.json mark.json
#   --break-at 100 --threshold-low 100 --threshold-high 100

# the new mark, if this report beat the stored one; exit 3 if it did not
scripts/mutation-ratchet.sh advance src/stryker-config.json report.json mark.json
```

A default-branch build that scores above the mark raises it. A pull-request build reads the mark and
is gated against it but never raises it — otherwise one branch in flight would move the bar for
every other open pull request.

The mark lives on the badges branch rather than in the source tree so that raising it never requires
CI to commit to the default branch or edit a tracked configuration file.

### Why there is no slack

The gate is `max(committed break, floor(mark - SLACK))`, and `SLACK` is **0**. It was 5 while the
score climbed from 46.58%; at 100% any slack does more harm than good.

**The smallest slack the gate can express already hides about 1% of all mutants.** Thresholds are
whole percentages (the derived value is floored), so the only gates above 99 are 100 and nothing.
With 983 mutants in scope, one mutant is 0.10 points:

| `SLACK` | Gate | Survivors that still pass |
| --- | --- | --- |
| 0 | 100 | 0 |
| 0.5 or 1 | 99 | 9 (974/983 = 99.08%) |
| 5 | 95 | 49 |

That count grows with the codebase — about 1% of it — so slack cannot be tuned to "a couple".

**Slack only buys anything if the measurement is noisy, and it is not.** The variance seen while
reaching 100% came from `static readonly` initialisers and EF's cached model (up to 16 mutants
between runs), both removed. The MTP under-reporting defect showed no divergence and is watched by
the weekly job, and timeouts count as detected. Every remaining source of noise can only lower the
score — a spurious failure, never a false pass — and slack does not cure that; it only hides real
regressions. If flakiness returns, fix its cause rather than reintroducing slack.

**Equivalent mutants in new code do not need slack either.** ADR 0005 already gives them a route:
a bare `// Stryker disable once` plus an ADR row with the reason, which review can see.

**The static threshold is still a floor, not a starting point.** A mark of 41.58 floors to 41 —
*below* a committed `break` of 42. Applied literally, the ratchet would **lower** the gate. The
`max()` keeps the bar from ever moving down.

**`high` has to rise too.** Stryker validates `break <= low <= high` *before* it mutates anything,
and exits `1` when that fails — which reads as a broken tool, not a threshold event. The familiar
half of that constraint is `break <= low`, which is why the two are always emitted as the same
number. The other half bites later and harder: with `high` at 80, a mark of 88 derives a `low` of 88
and Stryker refuses to start:

```
Threshold high must be higher than or equal to threshold low. Current high: 80, low: 88.
```

So `high` is raised to match whenever the gate would overtake it, and never lowered below its
configured value.

### When there is no mark — and when there is a broken one

`thresholds` treats "no mark" and "unreadable mark" alike: **no flags are emitted and
`stryker-config.json` governs**. A corrupt mark is reported on stderr; an absent one is not, because
absent is the normal state before the first advance and after a deliberate reset.

Emitting nothing is the point. The fallback does not re-state the static numbers, so there is
exactly one place the committed thresholds are written down and the script cannot drift from it.
It also means a bad file on an orphan branch degrades the gate to the committed floor rather than
wedging every merge — and no threshold is ever emitted below its own committed value.

**`advance` must not treat them alike, and does not.** Seeding from the committed floor is harmless
when there is genuinely no mark — it is how the first build records a real score. On a mark that
exists but cannot be read it is destructive: the emitted mark is force-pushed over the stored one,
so a single corrupt byte plus one default-branch build would rewrite a mark of 88 down to whatever
that build scored. `advance` exits `4` and writes nothing in that case. The only cost is that the
mark does not move until someone looks at the file.

Three independent guards keep the mark monotonic, because the consequence is irreversible and the
branch is force-pushed:

| Guard | Where | Catches |
| --- | --- | --- |
| Refuses to emit a lower mark | `mutation-ratchet.sh advance` | the ordinary case — this build scored less |
| Refuses to seed over an unreadable mark | `mutation-ratchet.sh advance` | corruption, a botched hand-edit |
| Refuses to publish a mark below the stored one | `publish-badges.sh` | anything that made the *read* come back empty |

The third exists because the first two can only reason about the mark they were **handed**, and CI
has to fetch that from the badges branch. If that read fails — an unreachable origin, a renamed
branch — a seeded advance arrives at the publisher looking perfectly valid. The publisher is the
last writer before the force-push and the only place that sees both numbers at once. CI also
refuses to guess: an origin it cannot reach fails the gate step rather than being read as "no mark".

An interrupted sweep cannot advance the mark either: `advance` goes through
`mutation-report.sh tally`, so the pending-mutant guard applies. Recording a score from a run that
did not finish would raise the bar permanently on a measurement that never happened.

### ⚠️ Lowering the mark by hand

Sometimes the mark should come down — a refactor that deletes well-tested code lowers the score
without lowering quality. There is no automatic path for this, by design.

**The badges branch is force-pushed by CI on every default-branch build.** Editing it is safe only
because CI *adds* files rather than rebuilding the branch; a push of your own that races a CI run
will be overwritten. Do it when no default-branch build is in flight, and check afterwards.

```bash
git fetch origin badges
git checkout badges

# lower it, or delete the file entirely to reset the ratchet to the committed floor
jq '.mark = 44' mutation-highwater.json > tmp && mv tmp mutation-highwater.json

git commit -am 'chore: lower the mutation high-water mark after <reason>'
git push origin badges
git checkout -        # do not leave the tree on badges; scripts/ does not exist there
```

Deleting `mutation-highwater.json` is the full reset: the next run falls back to
`stryker-config.json` and the next default-branch build above that floor sets a fresh mark.

Two things to know before editing by hand:

- **Delete the file rather than leaving it broken.** A file whose `.mark` is not a number is not
  treated as "no mark" — `advance` exits `4` and refuses to touch it, deliberately, so that
  corruption cannot quietly reset the ratchet. That is a stuck ratchet until someone fixes it,
  which is the intended trade.
- **Lowering it by hand works; lowering it from CI does not.** `publish-badges.sh` refuses to
  publish a mark below the one on the branch. Your edit is a direct commit to `badges` and is not
  subject to that guard — which is exactly why lowering is a manual act.

Record *why* in the commit message. The mark is a number nobody chose deliberately — that is the
accepted cost of a ratchet, and the commit log is the only place the reasoning survives.

## The weekly divergence check

`.github/workflows/mutation-divergence.yml` runs the sweep **twice on the same commit and the same
runner** — once at the concurrency CI normally uses, once with `--concurrency 1` — and compares the
**detected-mutant counts**. Equal counts mean the measurement is sound. Different counts mean the
score the pull-request gate has been enforcing is not measuring what we think it is, and the job
fails so a human finds out.

It runs weekly and on `workflow_dispatch`. It never runs on a pull request and never gates a merge.

### ⚠️ When to run it by hand, rather than waiting for Monday

Re-run it from the Actions tab (`Mutation divergence` → *Run workflow*) after either of these:

1. **A Stryker upgrade** — any change to the pinned version in `dotnet-tools.json` at the repository root.
2. **A significant change in the shape of the test suite** — deleting or splitting a test class,
   changing the test runner, or a large change in how many tests there are.

The second one is the unintuitive trigger, and it is the important one. **The defect is
test-shape-dependent, not version-dependent.** Upstream demonstrated an earlier release that
measured correctly and then started under-reporting once a test class was deleted — no dependency
change at all. So a version pin cannot guard this, and "we did not upgrade anything" is not a reason
to skip the check.

### Why it compares counts rather than scores

The upstream defect under-reports kills non-deterministically at concurrency above 1 on exactly this
stack (.NET 10, xUnit v3, Stryker 5.0.0). Its signature is that **killed-plus-survived stays
constant while the split between them moves** — so a single run looks perfectly self-consistent, and
a green build looks identical whether or not kills have started silently vanishing.

The score moves too, of course. But comparing scores would also fire on an ordinary quality change,
and comparing *detectable totals* would not fire at all. The detected count is the discriminating
number, which is why `scripts/mutation-report.sh compare` compares exactly that.

### What not to reach for when a run looks wrong

- ⛔ **Stryker's documented cross-check does not work for this.** The docs suggest re-running with
  coverage analysis disabled to verify a suspicious result. The upstream reporter showed it does not
  detect the concurrency defect, and we reproduced the same thing locally: disabling coverage
  analysis returned an identical score, which told us nothing about the runner.
- ⛔ **Do not bisect a score change file by file.** Scoping a run to a single file is reported
  upstream to lose kills even single-threaded, so the numbers you get from narrowing are not
  comparable to the numbers you started from. Read the HTML report instead.

### When it fails

The job summary carries both detected counts, both detectable totals and both scores, so the
divergence is diagnosable without downloading anything. Both reports are uploaded as artifacts for
when it is not.

When a sweep produced no *usable* report — it crashed, or was interrupted and left a report full of
pending mutants — the summary says so in that sweep's row and quotes the reason, and it does **not**
claim the measurement is sound. That distinction is deliberate: a file being present is not the same
as a file being trustworthy, and an interrupted run leaves one that exists and lies.

A failure means the gate has been enforcing a number that does not mean what it says. It does not
mean the build is broken — nothing merges differently because of it — so the response is to
investigate the measurement, not to rerun until it goes green.

⚠️ Note the job passes `--concurrency`, which is the one place in this repo that passes a
**measurement-affecting** flag to Stryker. Everywhere else the rule is that every value affecting
what gets measured lives in `src/stryker-config.json`, so a local run and the CI run stay
comparable. Here concurrency is the variable under test, so it has to come from the command line.
The ratchet's threshold flags are a separate category — they change the verdict, not the
measurement; see [The ratchet](#the-ratchet).

## Known weak spots

The worst-scoring files are real test debt, not noise:

- The DbContext's EF Fluent API configuration — 50 survivors, 0 killed. These encode real
  invariants (global uniqueness of storage root paths, the config-scoped content-hash index) and
  are killable with model-metadata assertions.
- The media type detector — 16 survivors.
- The video processor — 30 survivors.
