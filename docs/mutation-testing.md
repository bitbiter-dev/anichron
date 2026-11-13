# Mutation testing

Stryker.NET (backend) and StrykerJS (UI), both gated at 100%. How to work with survivors is in CLAUDE.md, "Mutation testing". This file covers how the setup works and why.

## Running it

- **Full sweep:** from `src/`, with its own `TMPDIR` (CLAUDE.md). Every setting that affects the measurement lives in `src/stryker-config.json`, so a local run judges exactly as CI does. Pass no flags.
- **CI runs Stryker only through `scripts/stryker-sweep.sh`.** It restores first and fails on `Analysis of project … failed`. Without that, Stryker silently leaves the project unmutated and still reports a score for the rest.
- Stryker exits `2` below `break`, so CI treats any non-zero exit as failure.
- The HTML report shows each survivor in its source context.

### The scoped local run

`scripts/mutation-changed.sh [--base <ref>] [--print] [source files…]`

- It collects every changed `.cs` file (committed, staged, unstaged, untracked) and runs Stryker once per project (ADR 0010). It runs the unit suite first, because one failing test fails every project's run.
- **Test → source mapping** for a changed `<Name>Tests.cs`: `<Name>.cs`, else the file declaring type `<Name>`, else the file named after the longest leading CamelCase part. A test it cannot map exits `1`; pass the sources it covers.
- **Mutation targets** are the projects where `IsMutationTarget` is `true` (`scripts/mutation-targets.sh`). The full sweep ignores the property, so an opted-out project also needs `!**/<Name>/**` in `stryker-config.json`.
- **What it cannot see:** a change that weakens the tests of an untouched file, or a change to `stryker-config.json`, a shared base class or a test double. Run the full sweep for those.
- **On a pull request** CI runs the same scope (`--matrix`). A change it cannot see, or any other non-C# or deleted file under `src/`, runs every target project instead. A weakened test of an untouched file surfaces only in the weekly full run.
- **Why a private `TMPDIR`:** the MTP runner passes the active mutant through `$TMPDIR/stryker-mutant-<n>.txt`. Two runs on one machine overwrite each other's mutant and report false survivors.
- **Why not `--since`:** inside a worktree it diffs the main checkout, tests 0 mutants and exits `0`. `--with-baseline` has the same flaw.

## The UI

- StrykerJS with the Vitest runner, configured in `src/Anichron.UI/stryker.config.json`. Scope: ADR 0014. Guard: `scripts/ui-mutation-guard.sh`.
- `scripts/ui-mutation-changed.sh` runs the whole scope in incremental mode (ADR 0014): a mutant whose code and covering tests are unchanged keeps its result from the baseline, `reports/stryker-incremental.json`.
- **The baseline** is the artifact `ui-mutation-incremental`, published by every `--all` run on master: the weekly full run, and a master push its PR did not test. A run without a local baseline seeds from the newest one.
- **What incremental cannot see:** a change to a view, a catalog or test support that turns a recorded kill into a survivor. The weekly full run catches it. A change to the measurement setup (the script's header lists it) runs every mutant.
- No `TMPDIR` hazard: each worktree has its own sandbox, and the mutant is passed in process.
- **`vitest.related` is off.** Per-test coverage already picks a mutant's tests; the related filter also drops a test file whose import of the mutated file Vitest cannot trace, such as a fresh-copy import with a query, and its mutants survive.
- **Test files share a worker** (`isolate: false`), so a leak between tests would count as a kill. The suite runs in shuffled order everywhere and must pass in any order.
- Both scoped scripts run their heavy commands under the machine-wide `heavy` lock (`scripts/with-lock.sh`), so parallel worktrees queue instead of competing for CPU.
- **Keep code out of import time.** Code run outside a test (module scope, a test file's top level, a request still in flight when a test ends) makes its mutants static: each one runs the whole suite. Build values in a function; ESLint rejects module-scope functions and literals in the scope. `src/test/setup.ts` settles `renderApp`'s queries before a test ends.
- **`timeoutMS` is generous on purpose.** A timeout restarts the runner, and the next mutant then runs on a cold Vitest and can time out too. Loops are caught by Stryker's hit limit, not by the timeout.
- **After a runner upgrade, prove the gate both ways:** a known survivor must fail the run, and a test inside a `describe` must kill (stryker-js#6265).
- Don't use `--logLevel debug`: it crashes the Vitest runner (stryker-js#6272).

## CI

- `Mutation targets` plans the runs (`scripts/mutation-changed.sh --matrix`): a pull request's changed files, otherwise every target, where an empty list fails. A pull request with nothing to mutate plans none and passes. `Mutation — <project>` runs one matrix job per planned project. `Mutation` merges the reports (`scripts/mutation-report.sh merge`) and fails in its **last** step, so the summary and report still upload.
- The merge refuses inputs it cannot trust: a missing project report, a file in two reports, a test id with two names, or differing schemas or thresholds.
- `Mutation` is not a required check itself. It blocks merges through `CI gate`.
- A master push whose tree passed `CI gate` on its PR runs none of this (`scripts/tested-on-pr.sh`); the weekly full run sweeps instead.
- A job-level `if:` does not relax `needs:`; only `always()` or `!cancelled()` does. That is why the weekly run's `publish` and `deploy-pages` use `!cancelled()` with explicit result checks.

## Configuration

| Setting | Rule |
|---|---|
| `test-runner` | `mtp`. Never `vstest`: it reports a silent 0.00% (ADR 0001) |
| `mutate` | Exclusions listed one by one (ADR 0005) |
| `ignore-methods` | Logging and unreachable throws (ADR 0005) |
| `thresholds` | `break`, `low`, `high` all `100` |
| `concurrency` | The hosted runner's vCPU count. Raise it only with the runner size: timeouts count as detected and inflate the score |

## The gate

### Why there is no slack

- Thresholds are whole percentages. 99 already lets about 1% of all mutants survive, and that count grows with the codebase.
- The measurement isn't noisy: the known noise sources were removed, and any remaining noise only lowers the score.
- An equivalent mutant has its own route: a suppression plus an ADR 0005 row.

## The weekly full run

`.github/workflows/mutation-divergence.yml` runs weekly on master and on demand, and never gates a merge.

- **Backend:** the solution sweep at CI concurrency, which fails below the thresholds, and the divergence check below.
- **UI:** `scripts/ui-mutation-changed.sh --all`.
- **Publishing:** coverage, the Pages site and the badges, from that sweep.
- **Failures:** `scripts/full-run-report.sh` opens "Full run failed: Weekly full run", assigned to the maintainer, or comments on the open one. A passing run closes it.
- A failed master push opens or comments on "Full run failed: CI on master" the same way. The PR that fixes it closes it.

### The divergence check

- The run also sweeps at `--concurrency 1` on the same commit, and fails when the **detected counts** differ.
- **Why counts:** the MTP defect (ADR 0001) keeps killed plus survived constant and moves the split between them, so a single run looks consistent. A score comparison would also fire on ordinary quality changes.
- This is the only place a measurement-affecting flag is passed.

### When to run it by hand

Run it from the Actions tab after:

1. A Stryker upgrade.
2. A significant change in the shape of the test suite: the defect depends on test shape, not on version.

Don't reach for these:

- Stryker's documented cross-check (coverage analysis off) does not detect the defect.
- Don't bisect a score change file by file: single-file runs lose kills upstream.
- A failure is a measurement problem. Investigate it; don't rerun until it's green.

## A survivor that only appears after a query-filter mutant

- EF caches compiled queries per process, and one test host serves many mutants. A query compiled while a `HasQueryFilter` mutant was active keeps the mutated filter, so later mutants survive in some runs and not others.
- Fix it in the test: an assertion that doesn't depend on the filter reads through `IgnoreQueryFilters()`. A fresh EF service provider per test also fixes it, but was rejected as far too slow.

## Publishing

- `scripts/mutation-report.sh` (`badge`, `compare`, `merge`, `tally`) refuses a bad measurement instead of returning a number: a missing or malformed report, partial thresholds, no detectable mutants, or pending mutants.
- `scripts/publish-badges.sh` adds the badges to the orphan `badges` branch, from the weekly full run on master. It keeps the last good badge when the payload is invalid, and refuses when origin is unreachable.
- `scripts/assemble-pages.sh` builds the Pages site (`coverage/`, and `mutation/` with `scripts/mutation-viewer.html`). It deletes only a directory that carries its sentinel `.assembled-by-anichron`. Its tests fake every dangerous path, `$HOME` included.

### Why the mutation report is optional

Without a sweep report, coverage still publishes and the landing page says the sweep failed. Failing the publish as well would only block on publishing; the sweep has already failed the run.

### Why it runs before the badge step

`publish-badges.sh` switches the work tree to `badges`, where `scripts/` does not exist. `pages-site/` is gitignored and survives the switch, because the script never runs `git clean`.
