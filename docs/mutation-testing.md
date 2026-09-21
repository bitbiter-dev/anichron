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

No flags are needed and none should be added. Every value that affects the score lives in
`src/stryker-config.json`, which is what makes a local run and the CI run comparable: CI passes
nothing on the command line but the output location.

> **Partly wired into CI.** The gate is configured to run on every pull request and fail below the
> `break` threshold (#180); the first hosted run has not been observed yet, so the `42` threshold
> is still calibrated against a single developer machine. The published report (#181), badge
> (#182), threshold ratchet (#183) and scheduled divergence check (#184) are specified under #177
> but not built — so today the report is a build artifact and the score appears in the job summary,
> with no URL and no badge.

The run takes roughly one to one and a half minutes on a developer machine, with live progress as
it goes. It finishes with the kill summary and the score; open the HTML report it prints the path
to for the detail — it shows each surviving mutant in its source context, which is how you find the
line that needs a test. The markdown report beside it has the per-file breakdown.

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

In CI the sweep runs with `continue-on-error` and the job's **last** step fails on the outcome.
Three constraints force that shape, and changing any part of it tends to break one of the others:

- GitHub applies an implicit `success()` to any `if:` without a status-check function. A step that
  aborted mid-job would therefore silently skip the formatting result, the coverage badge push and
  the Pages upload — so enforcement has to be deferred to the end.
- Enforcement must stay **inside the `Build & Test` job**, because that job is a required status
  check on the default branch. A separate gate job would turn the workflow red but could not block
  a merge until someone added it to the required-checks list.
- A job-level `if:` does **not** relax a `needs:` success requirement — only `always()` or
  `!cancelled()` does. So `deploy-pages` uses `!cancelled()`, guarded on the Pages artifact
  actually having been uploaded, which keeps the coverage report deploying when the gate fails
  while still skipping cleanly when an earlier failure meant no artifact was produced.

A mutation regression therefore fails a required check and blocks the merge, without suppressing
the formatting result, the coverage artifact, the badge or the Pages deployment. The Docker jobs
do get skipped, since they need `Build & Test` to succeed.

## Configuration

| Setting | Value | Why |
| --- | --- | --- |
| `test-runner` | `mtp` | **Required.** See [ADR 0001](adr/0001-mutation-testing-on-the-mtp-runner.md). |
| `mutate` | excludes `Migrations/**` | See [ADR 0002](adr/0002-mutation-scope-excludes-generated-append-only-code.md). |
| `thresholds.break` | `42`, against a measured 46.58% | Roughly five points of headroom. `low` is pinned equal to `break` because Stryker enforces `break <= low` and refuses to start otherwise. |
| `thresholds.high` | `80` | Report colour-coding only; it gates nothing. |
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

### Verifying the script

```bash
scripts/mutation-report.test.sh
```

Fixture-driven, no network, about a second. The fixtures in `scripts/fixtures/` are deliberately
tiny and hand-written rather than real reports — a real one embeds the full source of every mutated
file and runs to megabytes. `divergent-a.json` and `divergent-b.json` both hold ten mutants with
`Killed + Survived = 10` and differ only in the split (8/2 versus 5/5), reproducing the upstream
defect's signature so the comparison is tested against the thing it exists to catch.

## Known weak spots

The worst-scoring files are real test debt, not noise:

- The DbContext's EF Fluent API configuration — 50 survivors, 0 killed. These encode real
  invariants (global uniqueness of storage root paths, the config-scoped content-hash index) and
  are killable with model-metadata assertions.
- The media type detector — 16 survivors.
- The video processor — 30 survivors.
