# Mutation testing with Stryker.NET on the Microsoft.Testing.Platform runner

Status: accepted

## Decision

Merges are gated on a Stryker.NET mutation score, run with `test-runner: mtp`, although Stryker labels that runner "preview".

## Why

- **VSTest cannot see xUnit v3 failures.** xUnit v3 tests run out of process, so the default runner reports 0.00% with no error. Upstream has no plan to support xUnit v3 through VSTest.
- **No alternative exists.** Every other .NET mutation engine stopped releasing by 2022, and the active Stryker fork is VSTest-only.

## Consequences

- **Never switch back to `vstest`:** the score drops to zero silently.
- MTP has an open upstream defect that under-reports kills at concurrency above 1. It keeps killed plus survived constant, so a single run looks consistent.
- The defect depends on test shape, not version, so a version pin does not guard it. The weekly divergence check does (`docs/mutation-testing.md`).
