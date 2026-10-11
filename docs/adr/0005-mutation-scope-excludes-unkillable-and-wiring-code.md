# Mutation scope: 100% over code that can carry a signal

Status: accepted — supersedes [ADR 0002](0002-mutation-scope-excludes-generated-append-only-code.md)

The mutation target is **100%**. To make that number mean something, the scope excludes two kinds
of code that ADR 0002 kept in: code whose mutants **no test can kill**, and **pure wiring** whose
mutants carry no signal a unit test could act on. Everything else stays in scope, and anything that
survives in it is a missing test.

## Why ADR 0002 no longer fits

ADR 0002 excluded only generated, append-only code, and accepted a baseline in the forties as "the
intended pressure". That works for a ratchet that only has to avoid going down. It does not work
for a 100% target: the remaining mutants in the composition root can only be killed by booting the
host against a real PostgreSQL, which means Testcontainers, Docker inside the required `Build &
Test` job, and host-boot tests that Stryker re-runs for nearly every mutant. That is a large,
permanent cost for mutants that mostly assert "this service is registered".

## The rule

A file may be excluded when, and only when, one of these holds:

1. It is **generated and append-only** — the ADR 0002 rule, unchanged. Today: `Migrations/**`.
2. It is **pure wiring** — registration, configuration binding and host composition, with **no
   branching logic**: no `if`, `?:`, `??`, loop or exception filter beyond a framework lambda that
   only forwards. Logic found in such a file is extracted into a class that stays in scope, with
   its tests, *before* the file is added to the list.
3. It can only be exercised by **spawning real processes or talking to a real database**, and holds
   nothing beyond that adapter.

Exclusions are listed **one by one, by path**. No wildcard over a naming convention. ADR 0002's
objection to excluding the composition root still holds — wiring files grow logic, and a filename
exclusion would silently stop mutating it the day it arrives — so a new file is in scope until
someone deliberately adds it here, and the rule above is what they must show.

## What is excluded

### Files (`mutate` in `src/stryker-config.json`)

| File | Rule | Note |
| --- | --- | --- |
| `Migrations/**` | 1 | Unchanged from ADR 0002. |
| `Anichron.API/Program.cs` | 2 | Top-level host composition. |
| `Anichron.Worker/Program.cs` | 2 | Top-level host composition. |
| `Anichron.Core/Data/AnichronDbContextFactory.cs` | 2 | Used only by the EF design-time tools. |
| `Anichron.Worker/Ingestion/Proxy/SystemProcessLauncher.cs` | 3 | The `IProcessLauncher` adapter over `System.Diagnostics.Process`. |

### Calls (`ignore-methods`)

| Pattern | Why |
| --- | --- |
| `Log.*` | Calls into the source-generated `[LoggerMessage]` partials. Every class declares them on a nested `Log` class, so the qualified pattern matches exactly those. Asserting log output tests the logging framework, not the behaviour. |
| `UnreachableException.ctor` | The default arm of an exhaustive switch. Reaching it would require an enum value the type does not have. |

### Individual sites (`// Stryker disable once <Mutator>`)

The directive is left bare — the reason lives here, not in the code.

| Site | Why no test can kill it |
| --- | --- |
| The `finally { CryptographicOperations.ZeroMemory(passwordBytes); }` clauses in `Argon2PasswordHasher` and `PwnedPasswordClient` | Clears a buffer nothing reads afterwards; removing the statement or the whole block is equally unobservable. Suppressed per site rather than through `ignore-methods`, because `PasswordHasher` also wraps the timing-equalising throwaway Argon2 run in `ZeroMemory(...)`, and that call's mutant is killed and must stay in scope. |
| `FfmpegVideoProcessor.DetectEncoderAsync`: `encoder != H264Encoder.Software` | Software is last in the priority list, so the guarded log line is the only thing the condition controls. |
| `FileIngestionPipeline`: the `BoundedChannelOptions` initialiser and `SingleWriter = true` | Removing the initialiser leaves `FullMode` at its default, which is already `Wait`, and `SingleWriter` is an optimisation hint: either value produces the same results. The capacity arithmetic is **not** suppressed — `MaxConcurrentFiles / 2` is zero for a setting of `1`, which the channel rejects. |
| `= string.Empty` initialisers on `User`, `Invite`, `MediaAsset`, `ProxyFile`, `RefreshToken`, `UserStorageConfig`, `JwtSettings` and `WorkerSettings.User` | Placeholders that satisfy nullable analysis. Every real instance has these assigned, and `JwtSettings` is validated at startup before any value is read. |

`WorkerSettings.RootPath`, `ProxyPath` and `FfmpegPath` are **not** in that list. They are real
defaults and are tested.

## Consequences

- **`Log.*` hides four mutants that existing tests do kill**: the two log calls in
  `LoggingMiddleware`, `TokenCleanupService`'s deleted-count log, and `PwnedPasswordClient`'s
  "check unavailable" warning. Accepted — the tests stay and still guard those lines; they just no
  longer count toward the score. `LoggingMiddleware` is left with almost no mutants of its own as a
  result, which is expected for a class whose only behaviour is logging.
- **Ignoring calls changes the mutant set, not just the denominator.** With the statements inside
  a block ignored, Stryker generates block-removal mutants it used to filter out, so the detected
  count can move in either direction. Measured on `c084ee1`: detected went from 505 to 509 — eight
  new block-removal mutants, all killed, against the four ignored log kills above — and one new
  uncovered block in `ExifExtractionMiddleware`. The score moved from 45.58% to 50.20%. `scripts/mutation-report.sh compare` is therefore only
  meaningful between two runs of the **same** configuration; across a scope change, diff the
  mutants themselves.

- A file exclusion removes that file's mutants and nothing else. Every change to this list should
  be checked by comparing the mutants killed before and after, and any killed mutant that leaves
  scope must be named here.
- Every surviving mutant in scope is now a missing test or a missing entry in this ADR. There is
  no third category.
- Suppressing a site requires amending this ADR. A bare directive with no entry here is a review
  finding.
- The ratchet still gates at the stored mark minus five points. Raising it to enforce 100% is a
  separate change, made once the in-scope survivors are gone.
