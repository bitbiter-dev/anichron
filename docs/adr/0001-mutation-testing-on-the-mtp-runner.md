# Mutation testing with Stryker.NET on the Microsoft.Testing.Platform runner

Status: accepted — partially implemented

We gate merges on a mutation score from Stryker.NET, and we run it with
`test-runner: mtp` even though Stryker labels that runner "preview", because the default
VSTest runner cannot measure this repository at all and no alternative engine exists.

**Implementation state.** The runner choice and reproducible local sweep (#178) are in place, and
the CI merge gate is wired but its first hosted run is not yet observed (#180). The published
report (#181), badge (#182), threshold ratchet (#183) and the scheduled divergence check described
under Consequences (#184) are decided but **not yet built**. Read statements about those as the
decision, not as the current behaviour.

## Why not the default runner

xUnit v3 test projects always execute out-of-process. The in-process launch option that would
let an external tool observe individual test outcomes is exposed only to runner authors, not
through configuration. Stryker's VSTest integration therefore cannot see our tests fail.

Measured with Stryker 5.0.0. The VSTest rows are `Anichron.Core` alone, because the runner was
already disqualified before there was any point sweeping the whole solution with it:

| Configuration | Scope | Killed | Survived | Score |
| --- | --- | --- | --- | --- |
| `vstest` (default) | Core only | 0 | 435 | 0.00% |
| `vstest`, `coverage-analysis: off` | Core only | 0 | 99 | 0.00% |
| `mtp` | Core only | 21 | 57 | 21.21% |
| `mtp` | whole solution | 447 | 207 | 46.58% |

The failure mode is the dangerous kind: a plausible-looking 0.00% that reads as "the test suite
detects nothing" rather than "the measurement is broken". Stryker ships an automatic fallback
for exactly this situation (it disables coverage optimisation when capture appears to have
failed); that fallback is present in our pinned version and does not help. Upstream maintainers
state there is no plan to support xUnit v3 through VSTest and that MTP is the only way forward.

**Do not "fix" this by switching the runner back to the default.** That change produces a
score of zero with no error.

## Why not a different tool

No maintained alternative exists. The most recent release of any other .NET mutation engine
predates 2021 (Fettle, February 2020; Faultify, 2021; Testura.Mutation, 2022; VisualMutator,
2016; NinjaTurtles, 2015). The one actively published third-party fork of Stryker is VSTest-only
by its own documentation, so it hits the same wall. The real choice was mutation testing on a
preview runner, or no mutation testing.

## Consequences

The score's validity rests on a runner with an open upstream defect that non-deterministically
under-reports kills at concurrency above 1 on precisely our stack (.NET 10 + xUnit v3 +
Stryker 5.0.0). Its signature is that killed-plus-survived stays constant while the split
between them moves, so any single run looks internally consistent and no ordinary assertion
catches it. The documented upstream cross-check — re-running with coverage analysis disabled —
is known not to detect it.

We measured the defect as absent here: detected-mutant counts were identical at concurrency
1, 2, 4 and 8 across seven runs. But it is **test-shape-dependent, not version-dependent** —
upstream showed an earlier release degrading once a test class was deleted. A version pin is
therefore not a sufficient guard, so a scheduled job will re-run the sweep single-threaded and
fail on any divergence in detected count (#184).

Re-measure the baseline and re-run that comparison when upgrading Stryker **or** after a
significant change to the shape of the test suite. The procedure, and the caveats around
diagnosing a divergence, are in [mutation-testing.md](../mutation-testing.md).
