# Mutation scope: exclude generated, append-only code; include everything we author

Status: accepted

Mutation testing covers every project and every file we write. The only exclusion is code that
is both auto-generated and append-only — today that means the EF migrations directory, and
nothing else.

## Why this rule rather than a list

The rule exists because of what append-only generated code does to the *metric*, not because
those mutants are merely uninteresting. Migrations accumulate forever: the initial schema alone
contributes 347 mutants, 336 of them uncovered by any test and none of them ever likely to be
hand-tested. Included, the score drifts downward on pure schema evolution, so a ratcheted
threshold would eventually fail a pull request whose only content is a generated migration
containing no logic. The measurement stops being stationary, which makes it useless as a gate.

It is also consistent with the existing coverage configuration, which already excludes the
migrations namespace — though the two are not identical lists, and do not need to be. Coverlet
additionally excludes the generated OpenAPI namespace and anything attributed `GeneratedCode` or
`CompilerGenerated`; Stryker needs no equivalent, because source-generator output is never written
to disk as a project source file and so never enters mutation scope. Verified: of the 97 files in
the current report, none is generated.

Measured effect on the combined score:

| Scope | Score | Detected | Survived | No coverage |
| --- | --- | --- | --- | --- |
| Everything | 34.56% | 450 | 207 | 645 |
| Migrations excluded | 46.58% | 450 | 207 | 309 |
| Migrations + composition root excluded | 59.13% | 450 | 207 | 104 |

Note that `detected` and `survived` never move. Exclusions only shrink the denominator of
never-covered mutants; they cannot hide a tested weakness.

## What we deliberately do not exclude

Dependency-injection registration, host and application builder extensions, `Program`
entry points, the image and video processing helpers, and the EF Fluent API configuration in the
DbContext.

The third row above is the tempting one: excluding the composition root buys 12.5 points for no
loss of survivor signal, and nearly every project does it. We decline because those files hold
logic that grows — conditional registration, options validation, environment branches — and an
exclusion by filename would silently stop mutating that logic the day it is added. Worth noting
that `WorkerServiceCollectionExtensions` already scores 62% with 23 mutants killed, so a
wildcard pattern over registration files would have been actively wrong.

The DbContext's Fluent API configuration is the sharpest case: 50 survivors, zero killed, the
worst file in the repository. It stays in scope because it encodes real invariants we care about,
including the global uniqueness of storage root paths and the config-scoped content-hash index,
and because those mutants are genuinely killable with model-metadata assertions. It is test debt,
not noise.

## One combined score, not one per project

Stryker runs in solution mode, so all four projects are swept in a single invocation and produce a
single score gated by a single threshold. The alternative — a score and threshold per project —
was rejected as four numbers to maintain for a problem we have not had.

The cost is real and worth naming: a regression in one project can be masked by an improvement in
another, because only the total is gated. Core is currently the drag (its DbContext configuration
alone is 50 survivors), so an improvement elsewhere could hide a decline there. Accepted until
observed in practice; the per-file breakdown in the report is where masking would show up first.

## Consequences

The baseline is 46.58% rather than the 59% a more conventional exclusion set would report, and
the composition root contributes most of the uncovered mutants. That is the intended pressure.
Anyone proposing a new exclusion should show that the target is both generated and append-only.
