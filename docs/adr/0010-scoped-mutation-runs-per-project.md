# Scoped mutation runs go one project at a time

Status: accepted

## Decision

`scripts/mutation-changed.sh` runs Stryker once per changed project (`--project <Name>.csproj`), not once over the solution. The projects it may run are the mutation targets: those where `IsMutationTarget` is `true` (`Directory.Build.props`; `scripts/mutation-targets.sh`).

## Why

In solution mode, Stryker.NET captures per-test coverage once for every mutated project, and each capture runs every test of every assembly. A narrower `--mutate` doesn't help: the capture runs before the filter. One project per run cut a one-file scoped run to about a quarter of the time.

## Rejected

- `coverage-analysis: all`: every mutant then runs every test.
- Leaving only `Anichron.Testing` out of the solution run removes just one of the redundant captures.

## Consequences

- A change touching three projects runs Stryker three times, which is still cheaper than one solution run.
- CI runs one matrix job per target and merges the reports.
- The local full sweep stays in solution mode and ignores the property, so an opted-out project keeps its `!**/<Name>/**` entry (ADR 0005). `mutation-guard.sh` checks the two agree.
- Re-measure if a Stryker release captures coverage once per run.
