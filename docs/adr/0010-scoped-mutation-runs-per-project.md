# Scoped mutation runs go one project at a time

Status: accepted (2026-10-24, #309).

`scripts/mutation-changed.sh` runs Stryker once per changed project (`--project <Name>.csproj`),
instead of once over the solution with the changed files as `--mutate`. The projects it may run
are the mutation targets: those for which MSBuild evaluates `IsMutationTarget` to `true`
(`Directory.Build.props`, with `Anichron.Testing` opting out), listed by
`scripts/mutation-targets.sh`.

## Why

Stryker.NET 5.0 in solution mode captures per-test coverage once for each project it mutates
(`StrykerRunner.AnalyzeCoverage`). Every capture runs all tests of all test assemblies, one at a
time, because the MTP runner pool's `CaptureCoverageTestByTest` ignores the project it is given.
With five mutated projects, a run captures the same coverage five times. A narrower `--mutate`
does not help: the capture runs before the filter is applied.

Measured on one changed Worker file, same machine, back to back:

| Run | Mutants tested | Score | Wall time |
| --- | --- | --- | --- |
| Solution mode, `-m` for the file | 31 | 100.00% | 9:23 |
| `--project Anichron.Worker.csproj`, same `-m` | 31 | 100.00% | 2:16 |

On a quiet machine, a full sweep took 6:21, and about 1:45 of it was the four redundant coverage
captures. In CI it is about 5:15.

## Alternatives

- **`coverage-analysis: all`.** One aggregate coverage pass, but then every mutant runs every test,
  which is far slower than per-test coverage.
- **Leaving `Anichron.Testing` out of the solution-mode run only.** That removes one capture of
  five. The other four stay.
- **A fix in Stryker.** None is released, and no public issue reports it. We would keep solution
  mode until one is.

## Consequences

- A change touching three projects runs Stryker three times. Each run builds and captures
  coverage once, so it still costs less than one solution-mode run.
- Which projects get mutated is an MSBuild property, the same locally and in the CI matrix (#311).
  The local full sweep stays in solution mode and does not read the property, so an opted-out
  project keeps its `!**/<Name>/**` entry in `stryker-config.json` (ADR 0005).
  `mutation-guard.sh` fails when an opted-out project has no such entry, or when such an entry
  names a target project.
- If a Stryker release captures coverage once per run, solution mode may become cheaper again.
  This ADR is then worth measuring again.
