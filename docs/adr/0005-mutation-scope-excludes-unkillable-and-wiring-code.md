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
| `Anichron.API/Infrastructure/DatabaseStartupExtensions.cs` | 2 | Scope-and-resolve sequencing for migrate, seed and reset. The retry lives in `DatabaseStartupRetry`, in scope. |
| `Anichron.API/Infrastructure/WebApplicationBuilderExtensions.cs` | 2 | Loads `app.ini`. The defaults live in `ApiIniDefaults` and the path in `AppIniInitializer.PathUnder`, both in scope. |
| `Anichron.Worker/Infrastructure/HostApplicationBuilderExtensions.cs` | 2 | As above, with `WorkerIniDefaults`. |
| `Anichron.Infrastructure/Data/DatabaseFacadeExtensions.cs` | 3 | The PostgreSQL advisory-lock commands and connection handling. The polling loop lives in `AdvisoryLockMigrator`, in scope. |

### Calls (`ignore-methods`)

| Pattern | Why |
| --- | --- |
| `Log.*` | Calls into the source-generated `[LoggerMessage]` partials. Every class declares them on a nested `Log` class, so the qualified pattern matches exactly those. Asserting log output tests the logging framework, not the behaviour. |
| `*BeginScope` | Logging scopes such as `FileIngestionPipeline`'s per-file `W{WorkerIndex} {IngestionFile}`. Logging, on the same grounds as `Log.*`. |
| `UnreachableException.ctor` | The default arm of an exhaustive switch. Reaching it would require an enum value the type does not have. |

### Individual sites (`// Stryker disable once <Mutator>`)

The directive is left bare — the reason lives here, not in the code.

| Site | Why no test can kill it |
| --- | --- |
| The `finally { CryptographicOperations.ZeroMemory(passwordBytes); }` clauses in `Argon2PasswordHasher` and `PwnedPasswordClient` | Clears a buffer nothing reads afterwards; removing the statement or the whole block is equally unobservable. Suppressed per site rather than through `ignore-methods`, because `PasswordHasher` also wraps the timing-equalising throwaway Argon2 run in `ZeroMemory(...)`, and that call's mutant is killed and must stay in scope. |
| `FfmpegVideoProcessor.DetectEncoderAsync`: `encoder != H264Encoder.Software` | Software is last in the priority list, so the guarded log line is the only thing the condition controls. |
| `FileIngestionPipeline`: the channel capacity (`MaxConcurrentFiles * 2`), the `BoundedChannelOptions` initialiser and `SingleWriter = true` | Throughput only. Removing the initialiser leaves `FullMode` at its default, which is already `Wait`; `SingleWriter` is an optimisation hint; and any capacity produces the same results — including 0, which .NET 10 accepts as a rendezvous channel (measured; an earlier version of this ADR wrongly said it was rejected). |
| `= string.Empty` initialisers on `User`, `Invite`, `MediaAsset`, `ProxyFile`, `RefreshToken`, `UserStorageConfig`, `JwtSettings` and `WorkerSettings.User` | Placeholders that satisfy nullable analysis. Every real instance has these assigned, and `JwtSettings` is validated at startup before any value is read. |
| `ExifExtractionMiddleware.GetCoordinates`: the `catch` block | MetadataExtractor 2.9's `GetGeoLocation()` returns `null` for missing or malformed GPS tags rather than throwing, so no test input reaches it. Kept as a defensive guard: without it, a library change that throws would make the outer handler discard the file's entire EXIF. |
| `ExifExtractionMiddleware.GetOrientation`: the `>= ExifOrientation.Upright` bound | Tightening it to `>` only changes the result for tag 1, which then falls back to `Upright` — also 1. The directive sits inside the pattern, so the upper bound's mutants stay in scope. |
| `ProxyBound.FitTo`: the long/short-edge comparisons (`>=`, `<=`) | They only differ for a square image, where width and height are equal, so choosing either as the bounded edge gives the same size. The directives sit inside the conditional, so the `Edge == LongEdge` comparison stays in scope. |
| `ProxyBound.FitTo`: `bounded <= Pixels` | At exactly the bound, the mutant scales by `Pixels / bounded = 1`, which returns the source size the early return would have. |
| `FileIngestionPipeline`: the `"*"` search pattern | .NET treats an empty pattern as match-all, on the real file system and `MockFileSystem` alike (measured), so `"*"` → `""` changes nothing. |
| `IngestionPipelineRunner.TryRemoveIfEmpty`: `Exists && !EnumerateFileSystemEntries().Any()` | With `\|\|`, a missing directory throws `DirectoryNotFoundException` and a non-empty one fails `Delete` with an `IOException`; the same `catch` logs and swallows both. Only the log line differs. |
| Relationship and index configuration in `AnichronDbContext` that restates EF Core's conventions: the seven `HasMany`/`HasOne` calls whose cascade and foreign key match what the convention infers, the two `HasIndex` calls on foreign-key columns (which get an index anyway), and `base.OnModelCreating` (empty in `DbContext`) | Removing any of them leaves the model, and therefore the DDL, unchanged. They stay as explicit statements of intent, and the schema snapshot test against `docs/schema.sql` catches any future drift in EF's conventions. |

### Members that need real PostgreSQL (`// Stryker disable all` before each member)

| Member | Why it is not unit-tested |
| --- | --- |
| `AnichronDbContext.ExecuteInTransactionAsync` (both overloads) | Commit versus no commit is only observable on a provider with real transactions. |
| `EfRefreshTokenRepository.RevokeAllActiveByUserIdAsync` and `DeleteExpiredAsync` | `ExecuteUpdate`/`ExecuteDelete` are not supported by the EF InMemory provider. |

These are **not** tested against a second SQL engine such as SQLite. A substitute engine
diverges from PostgreSQL in exactly the places this model relies on (partial-index filters,
`xmin`, NodaTime types, collation), so a green test there would claim coverage it does not
have. They stay excluded until tests run against real PostgreSQL.

`WorkerSettings.RootPath`, `ProxyPath` and `FfmpegPath` are **not** in that list. They are real
defaults and are tested.

## Consequences

- **`Anichron.API/Infrastructure/ServiceCollectionExtensions.cs` is deliberately *not* excluded**,
  although it is wiring. It carries security configuration whose mutants are cheap to kill
  without a database: the JWT `TokenValidationParameters` flags, the admin authorization policy,
  the forwarded-header trust settings and the rate-limit policies. Tests already kill several of
  them. Its uncovered mutants are test debt for registration tests that build a
  `ServiceCollection` and resolve the configured options.

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
- **Mutants inside `static readonly` initialisers are unreliable.** The field is initialised
  once per test process, so a mutant there only takes effect if that process happened to
  initialise it while the mutant was active. The same mutants are reported killed in one run and
  survived in the next: on the same commit, the 16 extension mutants in `MediaTypeDetector`'s
  dictionary flipped between runs and moved the score by 1.6 points. Code under a 100% target
  should build such tables in a property or method, as `ApiIniDefaults` and `WorkerIniDefaults`
  do.
- **Stryker.NET binds a directive comment to the next syntax node, not to the following lines.**
  `// Stryker disable all` therefore covers one member, and `// Stryker restore all` has nothing to
  restore: each excluded member carries its own directive.
- **EF Core builds the model once per internal service provider and caches it.** A mutant in
  `OnModelCreating` only takes effect if the model is built while it is active, which is the same
  failure as a `static readonly` initialiser. Tests that assert on the model build their context
  with `EnableServiceProviderCaching(false)` so each one gets a fresh model.
- After the wiring extraction in #210 the score is 63.05% (617 killed, 4 timeouts, 234
  survived, 130 uncovered).
- Every surviving mutant in scope is now a missing test or a missing entry in this ADR. There is
  no third category.
- Suppressing a site requires amending this ADR. A bare directive with no entry here is a review
  finding.
- The ratchet still gates at the stored mark minus five points. Raising it to enforce 100% is a
  separate change, made once the in-scope survivors are gone.
