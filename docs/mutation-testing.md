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
`src/stryker-config.json`, which is what will make a local run and a CI run comparable once the
gate lands — CI is expected to pass nothing on the command line but the output location.

> **Mutation testing is not yet wired into CI.** Today this is a local command only. The CI gate
> (#180), published report (#181), badge (#182), threshold ratchet (#183) and scheduled divergence
> check (#184) are specified under #177 but not built.

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
what its documentation says — so whatever consumes the exit code should treat any non-zero value as
failure.

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

## Known weak spots

The worst-scoring files are real test debt, not noise:

- The DbContext's EF Fluent API configuration — 50 survivors, 0 killed. These encode real
  invariants (global uniqueness of storage root paths, the config-scoped content-hash index) and
  are killable with model-metadata assertions.
- The media type detector — 16 survivors.
- The video processor — 30 survivors.
