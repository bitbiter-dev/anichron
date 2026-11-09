# Mutation scope: 100% over code that can carry a signal

Status: accepted

## Decision

The mutation target is **100%**. The scope leaves out only code whose mutants no unit test can kill and pure wiring. Everything else stays in, and anything that survives there is a missing test.

## The rule

A file may be excluded only when one of these holds:

1. **Generated and append-only**: `Migrations/**`.
2. **Pure wiring**: registration, configuration binding and host composition, with **no branching logic** (no `if`, `?:`, `??`, loop or exception filter). Logic found in such a file is extracted, with its tests, before the file is listed.
3. **Only reachable through a real process or a real database**, and holds nothing beyond that adapter.
4. **Test support**: a library only test projects reference. It is tested by its own `*.Tests.Unit` project instead, because a mutated helper makes every test that uses it wait out its time limit.

Exclusions are listed **one by one, by path**, never by wildcard. A wiring file grows logic, and a filename pattern would silently stop mutating it.

## Files (`mutate` in `src/stryker-config.json`)

| File | Rule | Note |
| --- | --- | --- |
| `Migrations/**` | 1 | |
| `Anichron.API/Program.cs` | 2 | Host composition |
| `Anichron.Worker/Program.cs` | 2 | Host composition |
| `Anichron.Core/Data/AnichronDbContextFactory.cs` | 2 | EF design-time tools only |
| `Anichron.Worker/Ingestion/Proxy/SystemProcessLauncher.cs` | 3 | `IProcessLauncher` over `System.Diagnostics.Process` |
| `Anichron.API/Infrastructure/DatabaseStartupExtensions.cs` | 2 | Sequencing; the retry is `DatabaseStartupRetry`, in scope |
| `Anichron.API/Infrastructure/WebApplicationBuilderExtensions.cs` | 2 | Loads `app.ini`; defaults are `ApiIniDefaults`, in scope |
| `Anichron.Worker/Infrastructure/HostApplicationBuilderExtensions.cs` | 2 | As above, with `WorkerIniDefaults` |
| `Anichron.Testing/**` | 4 | Also sets `IsMutationTarget` to `false` (ADR 0010) |
| `Anichron.OpenApiGenerator/**` | 2 | Not shipped; `ApiDocumentTests` checks its output. Sets `IsMutationTarget` to `false` |
| `Anichron.Infrastructure/Data/DatabaseFacadeExtensions.cs` | 3 | Advisory-lock SQL; the polling loop is `AdvisoryLockMigrator`, in scope |

## Calls (`ignore-methods`)

| Pattern | Why |
| --- | --- |
| `Log.*` | Source-generated `[LoggerMessage]` calls. Asserting log output tests the framework. |
| `*BeginScope` | Logging scopes, on the same grounds. |
| `UnreachableException.ctor` | The default arm of an exhaustive switch. |

## Individual sites (`// Stryker disable once <Mutator>`)

The directive stays bare; the reason lives here.

| Site | Why no input can tell the mutant apart |
| --- | --- |
| `CryptographicOperations.ZeroMemory` in the `finally` of `Argon2PasswordHasher` and `PwnedPasswordClient` | Clears a buffer nothing reads again. Suppressed per site, because `PasswordHasher`'s timing-equalising `ZeroMemory(...)` is killable and stays in. |
| `FfmpegVideoProcessor.DetectEncoderAsync`: `encoder != H264Encoder.Software` | Software is last, so the condition only guards a log line. |
| `FileIngestionPipeline`: channel capacity, the `BoundedChannelOptions` initialiser, `SingleWriter = true` | Throughput only. Any capacity, 0 included, gives the same results. |
| `= string.Empty` on `User`, `Invite`, `MediaAsset`, `ProxyFile`, `RefreshToken`, `UserStorageConfig`, `JwtSettings`, `WorkerSettings.User`, `WorkerSettings.RootPath` | Placeholders for nullable analysis; every real instance assigns them. |
| `Positions.Of`: the `catch` block | MetadataExtractor returns `null` instead of throwing. It stays as a guard against a library change. |
| `ProxyStore.OpenRead`: the `catch` block | Returns `null`, like an emptied block. The rewrite that avoids it breaks S108. |
| `Frames.GetOrientation`: the `>= ExifOrientation.Upright` bound | `>` changes only tag 1, which falls back to `Upright`, also 1. |
| `FileIngestionPipeline`: the `"*"` search pattern | .NET treats `""` as match-all too. |
| `ServiceCollectionExtensions.AddAuthServices`: `AddSingleton(TimeProvider.System)` | The resilience handler `TryAdd`s the same instance. |
| `BurstDetectionHandler.Thumbnail`: `First` | Only candidates reach it, and a candidate always has a thumbnail. |
| `AnichronDbContext`: relationships and indexes that restate EF conventions, and `base.OnModelCreating` | Removing them leaves the DDL unchanged; the `docs/schema.sql` test catches convention drift. |

## Members that need real PostgreSQL (`// Stryker disable all` on each member)

Tested in `Anichron.Tests.Integration`, which is outside the Stryker run. Never tested on a substitute engine such as SQLite: it differs exactly where this model relies on PostgreSQL.

| Member | Needs |
| --- | --- |
| `AnichronDbContext.ExecuteInTransactionAsync` | Real transactions |
| `EfRefreshTokenRepository.RevokeAllActiveByUserIdAsync`, `DeleteExpiredAsync` | `ExecuteUpdate`/`ExecuteDelete` |
| `EfRefreshTokenRepository.TryRotateAsync` | A transaction, and row locking on the `RevokedAt IS NULL` guard, so two refreshes cannot both rotate a token (ADR 0017); what to rotate is `TokenService`, in scope |
| `EfBurstRepository.DeleteMemberlessAsync` | `ExecuteDelete` |
| `EfMediaAssetRepository.SoftDeleteAsync` (batched) | `ExecuteUpdate`; which rows are missing is `MissingFileSweep`, in scope |
| `EfWorkQueue.TryInsertAsync` | The partial unique index on `DedupKey` |
| `EfWorkItemCanceller.CancelAsync` | Raw SQL and row locking |
| `EfProxyOrphanStore.RecordAsync` | Raw SQL with `ON CONFLICT`; which files are orphans is `ProxySweepHandler`, in scope |
| `EfWorkItemStore`, all but `IsDueAsync`, and its `Running`/`Expired` filters | `FOR UPDATE SKIP LOCKED`, fenced `ExecuteUpdate`/`ExecuteDelete` |

## Consequences

- Every surviving mutant in scope is a missing test or a missing row here. A bare directive without a row is a review finding.
- `Anichron.API/Infrastructure/ServiceCollectionExtensions.cs` stays in scope although it is wiring: its security configuration is cheap to kill.
- `Log.*` hides a few mutants that tests do kill. The tests stay.
- Changing the scope changes the mutant set, so compare runs only within one configuration.
- A directive binds to the next syntax node, so each member carries its own.
- Mutants in `static readonly` initialisers and EF's cached model flip between runs. Build such data in a property or method, and model tests use `EnableServiceProviderCaching(false)`.
- No slack below 100: `docs/mutation-testing.md`, "Why there is no slack".
