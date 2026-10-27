# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## First-time setup

```bash
# Install local tools (Husky) and wire up the pre-commit hook
dotnet tool restore
dotnet husky install
```

## Build & Run

```bash
# Build the solution
dotnet build

# Run full stack (API + Worker + PostgreSQL)
docker-compose -f src/docker-compose.yml up

# Run API locally (http://localhost:5207)
dotnet run --project src/Anichron.API

# Run worker locally
dotnet run --project src/Anichron.Worker
```

## Testing

```bash
# Run the unit tests (the default suite)
dotnet test

# Run the integration tests (needs a running Docker daemon)
dotnet test -p:TestSuite=Integration

# Run both
dotnet test -p:TestSuite=All

# Run tests for a specific project
dotnet test src/SomeProject.Tests.Unit/SomeProject.Tests.Unit.csproj

# Run a single test
dotnet test --filter "FullyQualifiedName=Namespace.ClassName.MethodName"
```

An integration test project, or a filter matching integration tests, runs nothing without `-p:TestSuite=Integration` (or `All`).

**The git hooks are the pre-commit and pre-push gates; don't run them by hand as well.** `pre-commit` runs `dotnet format --verify-no-changes` and a full build, and `pre-push` runs the whole unit suite (`.husky/task-runner.json`). While working, run the test projects or tests you touched. Don't run the full unit suite before committing or pushing; `git push` runs it, and a failure stops the push. No hook runs the integration tests or mutation testing, so run those yourself when your change needs them.

Test projects are recognised by name: `*.Tests.Unit` for unit tests and `*.Tests.Integration` for integration tests. `Directory.Build.props` applies the test SDK to both: xUnit v3, **NSubstitute** (not Moq), FluentAssertions, `System.IO.Abstractions.TestingHelpers`, and coverlet. `Directory.Build.targets` makes only the projects in the selected `TestSuite` (`Unit` by default, `Integration` or `All`) test projects, so `dotnet test` and Stryker never run the integration suite. Internal members are exposed to test projects via `InternalsVisibleTo`.

**Unit or integration?** A test belongs in `Anichron.Tests.Integration` only when its subject is behaviour that only real PostgreSQL has: `ExecuteUpdate`/`ExecuteDelete`, transactions, advisory locks, row locking, constraint and partial-index enforcement. Everything else is a unit test, including a query's SQL shape (assert on `ToQueryString()` with `AnichronDbContexts.NpgsqlModel()`). The integration project starts one PostgreSQL container per run (`PostgresServer`, the same image as `src/docker-compose.yml`). Each test gets its own database, from `CreateMigratedDatabaseAsync` (real migrations) or `CreateDatabaseAsync` (empty). Its CI job is `Integration tests`, and it runs outside mutation testing, so the members it covers keep their `// Stryker disable all` and ADR 0005 rows.

Shared test infrastructure lives in `Anichron.Testing`, referenced by the test projects only and tested by `Anichron.Testing.Tests.Unit`; it is excluded from mutation (ADR 0005, rule 4). Use it instead of a private copy:

- **Time**: drive time-dependent code through a `Timeline` — its `Clock` is the fake `TimeProvider`, `Record` notes collaborator calls, `Watch` owns the task under test, and `ExpectAsync(...)` states what happens next and fails at the first event that differs. **No real-time waits in tests** (`WaitAsync(timeout)`, `SemaphoreSlim.WaitAsync(timeout)`, `Task.Delay`, an `await` on code that can hang): a broken implementation must fail at once, not when a guess runs out. Work delegates and substitutes used in loops yield and honour cancellation, so a loop that loses its delay cannot spin on the test thread. Recurring background work goes through `RecurringWork` (Worker) and is tested there.
- **Logging**: `FakeLogger<T>` from `Microsoft.Extensions.Diagnostics.Testing`.
- **EF**: `AnichronDbContexts.InMemory(...)` for data, `AnichronDbContexts.NpgsqlModel()` for model and SQL assertions; pass `freshModel: true` wherever a test asserts on the model.
- **DI scopes**: `ScopeFactories.Providing(services => ...)`, a real container rather than substitutes.

`Anichron.Architecture.Tests.Unit` holds the architecture rules (ArchUnitNET): decisions about types and dependencies that fail the build instead of living as prose. Adding one: `docs/agents/design-process.md`, "Adding an architecture rule".

### Mutation testing

```bash
dotnet tool restore
scripts/mutation-changed.sh   # scoped: only C# files changed since origin/master, one Stryker run per project
cd src && TMPDIR="$(mktemp -d)" dotnet stryker   # full sweep; must run from src/, not the repo root
```

⚠️ **Never run `dotnet stryker` with the shared `TMPDIR`.** Stryker's MTP runner passes the active mutant through `$TMPDIR/stryker-mutant-<n>.txt`, a path every run on the machine shares, so two agents mutating at the same time corrupt each other's results (survivors that tests do kill, scores down to 0%). `mutation-changed.sh` isolates itself; give a direct run its own `TMPDIR` as above. See `docs/mutation-testing.md`.

`scripts/mutation-guard.sh` (a CI step) enforces the setup rules: every setting lives in `src/stryker-config.json`, so a local run judges exactly as CI does; the `mtp` runner (`vstest` silently reports 0.00% under xUnit v3, ADR 0001); thresholds at 100; no `--since`/`--with-baseline` (in a worktree they test 0 mutants and exit `0`); `--concurrency` only in the divergence job, `--mutate` only in `mutation-changed.sh`, and `--project` only there and in CI's per-project matrix; workflows run Stryker only through `scripts/stryker-sweep.sh` (restores first, fails when Stryker could not analyse a project and silently left it unmutated); an ADR 0005 entry for every Stryker directive and excluded path; and a `!**/<Name>/**` exclusion for exactly the projects that set `IsMutationTarget` to `false` (ADR 0010). When it fails, fix the setup, not the guard.

#### Surviving mutants: test first, exclude only when equivalent

The gate is **100%**: one surviving or uncovered mutant fails CI. Before finishing any change to production code, run `scripts/mutation-changed.sh` and work through every undetected mutant in the files you touched. It maps a changed `<Name>Tests.cs` to `<Name>.cs` and exits `1` for a changed test file it cannot map; pass the source files that test covers, relative to `src/`, as arguments. A changed file in no mutation target project (`scripts/mutation-targets.sh`) is named as `not mutated` and skipped. The full sweep in CI is the authority; run `dotnet stryker` locally as well only when the change reaches beyond the files it touches (`stryker-config.json`, a shared base class, test doubles):

1. **Write a test.** A survivor is a missing test until proven otherwise. The test asserts behaviour that the mutant changes, with an expected value from an independent source (a literal, the spec), never recomputed the way the code does.
2. **Question an exclusion only for an *equivalent* mutant**: one where no input can make the original and the mutant behave differently. Prove it by naming why every input agrees (a square image, a value exactly at the bound, a branch that only logs), or by measuring it. "Hard to test" is not equivalence.
3. **Prefer a rewrite that removes the equivalent branch** without changing behaviour (a clamp instead of a guard, an early return instead of a logging-only `if`). Suppress only when no such rewrite exists.
4. **Suppress narrowly.** Add a bare `// Stryker disable once <Mutator>` before the statement, and a row in ADR 0005 saying why. The directive binds to the next syntax node and disables *every* mutation of that mutator on it, so first confirm no killable sibling mutation on the same node gets hidden; if one would, restructure instead. A whole-file exclusion is only for pure wiring with no branching logic, listed by path in `stryker-config.json` and in ADR 0005.
5. **Code that only real PostgreSQL can exercise** (transactions, `ExecuteUpdate`/`ExecuteDelete`) gets a per-member `// Stryker disable all` and an ADR 0005 row. It is not tested on SQLite or another stand-in engine.

Done when the scoped run reports `100.00%` (or nothing to mutate) and every new directive or exclusion has its ADR 0005 row. Lowering the thresholds is never the fix; the reason is in `docs/mutation-testing.md`, "Why there is no slack".

Mutants that flip between runs come from `static readonly` initialisers, EF's cached model, and EF's compiled-query cache (a killing assertion that does not depend on the soft-delete filter reads through `IgnoreQueryFilters()`, #312): build such data in a property or method, and build model-asserting test contexts with `EnableServiceProviderCaching(false)` (`AnichronDbContexts.NpgsqlModel()`, or `InMemory(freshModel: true)`). An assertion behind `?.` (`x?.Value.Should()...`) never runs when `x` is null; assert presence first (`ContainSingle(...).Which`). Rationale for every existing exclusion: `docs/adr/0005-mutation-scope-excludes-unkillable-and-wiring-code.md`.

## Architecture

Four projects with strict separation of concerns:

| Project | Role |
|---------|------|
| `Anichron.Core` | Single source of truth: domain models, `AnichronDbContext`, all Fluent API config, shared utilities (XXHash64, path parsing). Zero dependencies on other projects (every other project references Core, so a reference back would be circular and fail the build). |
| `Anichron.Infrastructure` | Shared host plumbing used by API and Worker: configuration (`DatabaseConfiguration` reads the connection from `POSTGRES_CONNECTION__*` env vars or Docker secrets; INI bootstrap; `ConfigurationList`), migration under a PostgreSQL advisory lock, and the API's read-only stores (`IProxyStore`, `IOriginalStore`). Each host wires its own DI. |
| `Anichron.API` | ASP.NET Core Minimal APIs (the only web project: `libraries-are-not-web` keeps ASP.NET Core out of Core, Infrastructure and Worker, and the two hosts never use each other) — owns all identity/auth/admin writes (users, storage configs, refresh tokens, invites, lockout); read-only for media data. Media endpoints so far: proxy serving (#43), On This Day (`GET /api/v1/flashbacks/on-this-day`, #38), a burst's members (`GET /api/v1/bursts/{id}/assets`, #299) and the caller's star/like/hide state (`PATCH /api/v1/assets/{id}/interaction`, #46); originals (#44) are deferred past v1.0. All routes prefixed `/api/v1/`. |
| `Anichron.Worker` | `BackgroundService`s — NAS crawling, EXIF extraction, FFmpeg transcoding, proxy generation, burst detection, reconciliation (soft-delete and auto-restore), admin-requested proxy sweeps and deletions, and the work queue's executor and scheduler (maintenance such as expired-refresh-token cleanup runs as work items) |

**Write ownership is split by domain, not centralized.**
- **Worker is the only writer of *media* data** — `MediaAsset`, `Metadata`, `ProxyFile`, `Burst`, `BurstCheck`, plus all reconciliation and soft-deletes happen here. The API never writes media: outside its DI wiring it never touches `AnichronDbContext` (architecture rule `api-reaches-data-through-repositories`), and the media write repository stays out of it (`only-worker-writes-media`). Its one identity-side write is the `TokenCleanup` work item's handler deleting expired refresh tokens.
- **API owns all *identity/auth/admin* writes** — registration, login/refresh-token issuance and revocation, lockout state, invites, and admin user/storage-config CRUD all persist through API services (`AuthService`, `AdminUserService`, `AdminStorageConfigService`, `TokenService`, etc.) via `IUnitOfWork.SaveChangesAsync`. The API is read-only *only with respect to media*. The one exception is `AssetInteraction` (star/like/hide, #46): per-user state, written by the API through `IAssetInteractionWriter` (Core), which takes and returns plain records so no API type touches the entity. It checks only `VisibleTo` (never `NotHiddenFor`, so a hidden asset can be unhidden), and a first write that loses the `(UserId, AssetId)` unique race reloads and reapplies once.
- **Work items** are inserted only through `IWorkQueue` (architecture rule `only-work-queue-inserts-work-items`); the API's only other write is an admin's cancel through `IWorkItemCanceller` (`work-items-change-only-through-queue-or-cancel`: outside the Worker, only `EfWorkQueue`, `EfWorkItemReader` and `EfWorkItemCanceller` depend on the `WorkItem` entity; raw SQL naming the table is beyond the rule and is for review); every other state change belongs to the Worker's executor, and handlers live only in the Worker (`work-handlers-live-in-worker`). See "Work queue" below.
- **Proxy orphans** (`ProxyOrphans`, a proxy sweep's report) are written only by the Worker; outside it only `EfProxyOrphanReader` depends on the entity (`only-worker-writes-proxy-orphans`). See "Proxy sweeps" below.
- Both hosts apply EF migrations at startup, serialised by a PostgreSQL advisory lock (`MigrateWithAdvisoryLockAsync`).

A planned `Anichron.UI` (NextJS/React) for the Instagram Story-style flashback experience is documented but not yet in the solution.

### Domain Model (`Anichron.Core/Domain/`)

📌 **`docs/schema.sql` is the authority on columns, types, indexes and delete behaviours.** It is
`dotnet ef dbcontext script` output, regenerated by `scripts/derive-db-docs.sh` and gated in CI by
`--check`, so it cannot drift. The prose here is orientation only — where the two disagree, the
generated file is right. **Do not hand-maintain a column list in this file.**

📌 **Rationale belongs in the model, not in prose.** Table and column `COMMENT`s are configured
with `.HasComment(...)` in `AnichronDbContext`, beside the Fluent config they explain. They travel
into PostgreSQL and therefore into `docs/schema.sql` and any schema-doc tool pointed at the
database. If you find yourself about to explain *why* a column exists in a Markdown file, put it in
a `HasComment` instead — see `docs/adr/0004-schema-documentation.md`.

**Twelve entities**, all EF Core config via **Fluent API only** (architecture rule `domain-has-no-data-annotations`). Columns are
**PascalCase** — the only explicitly-named column in the whole model is `Invites.xmin`. Table names
do not always match entity names (`AssetInteraction` → `Interactions`, `UserStorageConfig` →
`StorageConfigs`):

- `MediaAsset` — central entity: `FilePath`, `ContentHash` (XXHash64), `Month`/`Day`/`Year` integers (set only by the `DateCaptured` setter), `MediaType`, `IsSoftDeleted`, and a `PairedAssetId` self-reference (HEIC parent → MOV child)
- `Metadata` — 1:1 with `MediaAsset`; EXIF data (dimensions, GPS, raw EXIF orientation tag 1–8, camera make/model/lens, video duration) and the image's `Blurhash` string (a column, not a proxy file)
- `ProxyFile` — one row per generated artefact, each a file at `ProxyPath` on the local SSD. `ProxyType` is `Thumbnail`, `WebVideo` or `FullPreview`
- `Burst` — groups photos of one storage config taken moments apart, so a flashback shows one memory; `PrimaryAssetId` is the stored cover (the earliest member), nullable with `SET NULL` so deleting it never fails
- `BurstCheck` — one row per photo burst detection has checked (no row: unchecked), with whether it is a candidate and Apple's `BurstUUID` (`CameraBurstId`); cascades with its asset
- `AssetInteraction` — per-user state (starred, liked, hidden, view count, last viewed); unique on `(UserId, AssetId)`
- `UserStorageConfig` — NAS root paths per user; `RootPath` is **globally unique** — a path cannot be assigned to more than one storage config across all users. Paths are stored canonical (absolute, `.`/`..`/`//` collapsed, no trailing slash) via `StoragePath.Canonicalize`, so `/p` and `/p/` cannot both be assigned. A `RootPath` must be a folder strictly inside `StoragePath.OriginalsRoot` (`/data/originals`), never the root itself, and may not contain or be contained by another config's root (`StoragePath.Overlaps`) — nested roots would index the same files twice
- `User` — owns storage configs and interactions
- `RefreshToken` — issued and revoked by the API, expired rows deleted by the Worker's `TokenCleanup` work item; `TokenHash` unique
- `Invite` — admin-issued registration tokens; `TokenHash` unique, and the only entity carrying an optimistic-concurrency token (`xmin`)
- `ProxyOrphan` — one file a `ProxySweep` found under the proxy root that no `ProxyFile` row references, with its size and last write time at the scan and, once a `ProxyDeletion` processed it, its outcome; cascades with the sweep's work item
- `WorkItem` — one durable unit of Worker work (`Kind`, jsonb `Payload`, optional `DedupKey`, optional `StorageConfigId` scope that cascades on config delete, status, lease, attempt, progress snapshot, result or error); see "Work queue"

### Key EF Core / Database Decisions

- `Month` and `Day` stored as separate integers on `MediaAsset` with a **composite index** (`IX_MediaAsset_Flashback`) — enables "On This Day" queries as direct index lookups instead of `EXTRACT()` scans
- Partial unique index on `(StorageConfigId, ContentHash)` filtered to `"IsSoftDeleted" = false` (`MediaAssetIndexNames.ActiveContentHashUnique`) — enforces one *active* asset per content within a storage config; it is what lets a moved file be re-linked to its existing row by hash (#35). Dedup is **config-scoped**: the same bytes under two storage configs are two assets, which is what makes multi-user work. A *global* unique index on `ContentHash` alone would break that (pinned by `ModelInvariantsTests`). When two consumers race on the same content, the loser's insert hits this index and `IngestionSaver.TrySaveNewAsync` skips the file without throwing, so pipeline compensation never deletes the winner's shared proxies
- Unique index on `(StorageConfigId, FilePath)` — one asset row per path within a config, soft-deleted rows included
- Unique index on `ProxyFiles (AssetId, ProxyType)` (`ProxyFileIndexNames.OnePerAssetAndType`) — at most one proxy of each kind per asset, so two attempts racing to add the same proxy (e.g. a paired MOV's backfilled `WebVideo`) cannot both land; the loser is a `LostIngestionRace`
- Non-unique partial index on `(StorageConfigId, ContentHash)` filtered to `"IsSoftDeleted" = true` (`MediaAssetIndexNames.SoftDeletedContentHash`) — lets the classifier find soft-deleted rows by content for auto-restore without reading the config's rows; `FindByHashesOrPathsAsync` keeps one `OR` branch per index (active hash, soft-deleted hash, path) so PostgreSQL combines their scans, checked by an `EXPLAIN` integration test
- Filtered index `IX_MediaAssets_Active` on `IsSoftDeleted`, filtered to `"IsSoftDeleted" = false` — excludes trashed assets from active gallery queries
- Global query filters hide soft-deleted assets: on `MediaAsset`, and on `Metadata`, `ProxyFile`, `AssetInteraction`, `BurstCheck` and `Burst` through their asset (a burst whose cover is null stays visible). This is the **only** global filter (pinned by `ModelInvariantsTests`) — per-user rules (hidden, ownership) are query-level, because the Worker shares `AnichronDbContext` and has no user
- `User.Username` and `User.Email` carry an EF value converter (`IdentityNormalizer.Normalize`: trim + lowercase) that applies to every write **and** to every equality comparison, so repository lookups need no manual normalization. ⚠️ The InMemory provider does not convert query parameters, so read-side behaviour is tested via `ToQueryString()` on the Npgsql provider, not InMemory
- **NodaTime for every date and time.** `System.IO` hands back `DateTime` (e.g. a file's last-write time); convert it at that boundary. Domain types never hold one (architecture rule `domain-uses-nodatime`)

### Storage Layout

```
/data/originals/   ← NAS (mounted :ro by both API and Worker — originals are never modified)
  <user>/          ← one mount per user, at the SAME container path in the API and that user's worker
/data/proxies/     ← Local SSD (Worker writes; API reads for serving) — fixed StoragePath.ProxiesRoot, not a setting, so both always agree
```

Proxy files follow a two-level shard path derived from **content, not from the ingestion attempt**: `/data/proxies/{content_hash[0:2]}/{storage_config_id:N}-{content_hash}/{type}` (e.g. `01/3fa85f6457174562b3fc2c963f66afa6-0123456789abcdef/thumbnail.jpg`). Re-ingesting the same file therefore resolves to the same directory, so a retry overwrites its own debris instead of stranding it; the storage config id keeps byte-identical files of two users apart, without which removing one user's config would cascade away the other's proxies. `IProxyDirectoryStrategy` is the single place this rule lives. The API serves them at `GET /api/v1/assets/{id}/proxies/{thumbnail|preview|video}` (visibility rule first, then only the stored `ProxyPath` under the proxy root; strong `ETag`, `private, immutable` caching, range requests). `ProxyFile.ProxyPath` stores the path relative to `/data/proxies/` (note: `FilePath` is on `MediaAsset`, relative to the storage config root — the two are different properties).

The API reads files only through two narrow stores in `Anichron.Infrastructure/Storage/`, never through `IFileSystem` (architecture rule `api-reads-no-file-system`). `IProxyStore.OpenRead(proxyPath)` owns the join with the proxy root, the containment check and "missing means `null`"; `IOriginalStore` has only `RootExists()` until serving originals (#44) adds its first reader. Neither has a write method. A new kind of read is a new store method with its own tests, not a general file system. See ADR 0007.

### Worker Media Processing

- Crawls each storage config as its own `Crawl` work item (#281), due every `Worker:CrawlIntervalHours` (default 4 h) after the last one finished; the first crawl after a start comes from due-ness, so a restart that outlasted the interval crawls straight away. Progress is items found, processed and failed (a Live Photo pair is one item), with no total until the walk ends
- The crawl walks folder by folder and never enters `CrawlExclusions`: any dot-folder or dot-file (macOS/Linux/QNAP/ZFS/sync-tool metadata), NAS and OS system folders (`@eaDir`, `#recycle`, `@Recycle`, `$RECYCLE.BIN`, `lost+found`, Nextcloud `files_trashbin`/`files_versions`, …) and app library packages (`*.photoslibrary`, `*.lrdata`, …, which hold thumbnails and renders). `Worker:ExcludedDirectories` adds comma-separated folder names (not paths; rejected at startup) on top. An unreadable subfolder is logged and skipped; an unreadable or missing root fails that storage config's `Crawl` item with the error, and the other configs' items still run (#282)
- Computes XXHash64 `ContentHash` → extracts EXIF via MetadataExtractor → writes `MediaAsset` + `Metadata`
- Generates proxy types: `Thumbnail`, `FullPreview`, `WebVideo` (the `ProxyType` enum, persisted as text), and a BlurHash string for images, stored in `Metadata.Blurhash`
- Image proxies are auto-oriented once after decoding: upright pixels, orientation tag 1. `Metadata.Orientation` applies only when serving originals — never rotate a proxy by it
- Image proxy sizes are edge-based, so portrait and landscape get the same treatment: preview long edge `PreviewLongEdge` (1920), thumbnail short edge `ThumbnailShortEdge` (300), blurhash sample long edge `BlurhashSampleLongEdge` (64). Nothing is ever upscaled
- Each `IImageProxyGenerator`, and the `BlurhashEncoder`, declares a `ProxyBound`; `ImageProxyMiddleware` decodes once to `Rgb24`, shrinks the shared image to each bound largest-first, and hands every step **its own copy** at that size. The encoder's string goes on the `IngestionContext` and the content writer (`ProcessedContentWriter.ContentOf`) puts it in the content's `Metadata`. Generators may crop/recolour/resize their copy freely; only the middleware resizes the shared image
- FFmpeg transcodes video with runtime GPU detection: QuickSync (`h264_qsv`) → NVENC (`h264_nvenc`) → AMF (`h264_amf`) → software (`libx264`)
- Burst detection (#297, #298): a `BurstDetection` work item per storage config, enqueued by the config's `Crawl` when it finishes and scheduled at the crawl interval as a fallback (one dedup key, so never twice). `BurstDetectionHandler` reads and writes through `IBurstRepository` (Core). It first repairs the config's bursts (fewer than 2 active members: deleted; otherwise the earliest active member becomes the cover; bursts with no member rows, in any config, are deleted in one `ExecuteDelete` so concurrent Workers cannot conflict), then checks each unchecked photo (`Image` or `LivePhoto`, never `Video`, so a paired MOV never joins) oldest first, in batches: it reads the original once through `IOriginalPhotoReader` (EXIF capture time, Apple `BurstUUID`) and writes its `BurstCheck` row. A candidate (EXIF capture time, camera make and model, a thumbnail) joins every checked candidate of the config with the same `BurstUUID`, regardless of time. It is then compared with every checked candidate of the same camera make and model captured within `Worker:BurstGapSeconds` (120), except those its label already groups (a labelled frame or a member of its burst), through `IThumbnailMatcher` (each score logged at `Debug`, counted as matched or not), and joins each one scoring at least `Worker:BurstSimilarityThreshold` (0.25), oldest first, unless the joined burst's active members would span more than `Worker:BurstSpanSeconds` (300, at least the gap, checked at startup); camera-label joins ignore the span. Joins create or merge bursts (into the oldest, cover the earliest member); each photo saves with its burst changes, so checked pairs are never compared again. `OrbThumbnailMatcher` is the only OpenCV code (architecture rule `opencv-stays-in-the-matcher`, ADR 0009): ORB keypoints on the stored thumbnails, a ratio test and a RANSAC homography, the score being the share of keypoints that fit it; it describes each thumbnail once per batch. A missing root fails the item before anything is marked. Per-config handlers skip a missing or inactive config through `ActiveStorageConfigs`, returning `StorageConfigSkipped`.
- Reconciliation (#33, ADR 0011): classification reconciles every crawled file against the config's rows, top to bottom, first match wins: an active row with its hash at its path is `Unchanged` (`LastSeenOnNas` refreshed). With that row's file still on disk at another path Q, an active row at the crawled path P is soft-deleted (`OverwrittenByCopy`: `cp Q P`), otherwise the file is a `DuplicateCopy` (reported by #36). With Q gone, a row at P takes Q's content (`OverwrittenByMove`: `mv Q P`, **the path holder wins**): hash, capture date, media type, `Metadata` and proxy paths copied, not regenerated, burst mark cleared, `Id`, interactions and `PairedAssetId` kept; Q's row is soft-deleted first and P's row saved second, in one transaction (`IngestionSaver.TrySaveRetiringFirstAsync`), and a soft-deleted P is auto-restored. With no row at P, Q's row moves (`Moved`, #35). With no active row holding the hash, an active row at P was **edited in place** (`ContentChanged`, #250): processed like new content, and `ContentChangedWriter` updates that row (new hash, capture date, media type, `Metadata`, proxy rows by type; `Id`, interactions and `PairedAssetId` kept; burst mark cleared). A soft-deleted row at P is **auto-restored**: as it was (`AutoRestored`, proxies and `Metadata` reused) or with its content changed (`AutoRestoredContentChanged`, through `ContentChangedWriter`). Otherwise the most recently seen soft-deleted row with the hash is auto-restored at P (`AutoRestoredMoved`), else the file is `New`. Ahead of all of this, **a video crawled alone whose matched rows include a Live Photo's video** (a row some photo, active or soft-deleted, points at through `PairedAssetId`: `MatchedAsset.IsLivePhotoVideo`, an `EXISTS` in the same query) is a `LoneLivePhotoVideo` (#321): that row is never refreshed, moved, restored or retired, since it follows only its photo; an active row at P that is not one is soft-deleted, as a copy of the video over it; the path counts as found. The same holds for a Live Photo's MOV half when the matched Live Photo's video is not the one the item's photo row points at (as after `cp B.heic A.heic`), and its pairing is then `None`, so it is never restored only for the sweep to retire it again. Auto-restore clears `IsSoftDeleted`, refreshes `LastSeenOnNas` and deletes the `BurstCheck` row; interactions are untouched, so a hidden asset stays hidden. After ingestion, `MissingFileSweep` soft-deletes, with one `ExecuteUpdate` per batch, the config's active rows whose path the walk did not find (both Live Photo halves of a queued item and failed items count as found; excluded folders do not), plus every active video that a missing or soft-deleted photo points at, found or not, so a Live Photo goes as one (#321), unless the walk skipped an unreadable folder (`IncompleteWalk`) or found no media while the config has active rows (`EmptyRoot`). The crawl's `Result` carries `reconciliation { softDeleted, autoRestored, skippedBecause }`, logged in one `Information` line; counts are rows, so a Live Photo counts 2. Each soft-deleted path is logged at `Debug`, a soft-delete at classification at `Information`, and so is a Live Photo's video soft-deleted (missing) or auto-restored beside an active photo, which makes the photo a still photo or a Live Photo again. **Live Photo MOV halves** keep `DuplicateCopy` and `PathTaken` instead of the overwritten outcomes, and an edited MOV keeps its stored row unchanged (with a soft-deleted row at its path, it is auto-restored with that content); so a Live Photo moved onto another keeps the old video until treating a Live Photo as one unit lands (#331, the follow-up to #303). A MOV half is reconciled before its HEIC is processed. A move onto a path a concurrent consumer took after classification fails the save, and the item is retried on the next crawl; two rows taking one hash at once lose as a `LostIngestionRace`, skipped until the next crawl. Swapping two paths' contents can soft-delete both rows for one crawl; the next auto-restores them
- Duplicate content is **reported, never merged** (#36). `SharedContentReporter` runs at the end of each config's `Crawl` item: it logs one `Information` summary for the crawled config (copies within it, counted by that crawl from each item it ingested, and pairs of its active rows sharing a `ContentHash` with the same user's other active configs), with each pair at `Debug`, and the counts go into the crawl's result. A pair across two configs is reported by each of their crawls. A failed report is logged and does not fail the crawl. Duplicates across different users are expected and never reported. Two new identical copies racing in one crawl are first reported on the next crawl
- Failure ownership: `ProxyStagingWriter` is the single place a proxy reaches its final name (architecture rule `proxies-reach-final-name-only-in-staging-writer`) — it writes under a per-attempt `.<attempt id>.tmp` suffix (so concurrent attempts on the same content never share a temporary file), renames into place once complete, and registers the proxy on the `IngestionContext` at that moment; the proxy middlewares only supply the bytes. If a step throws, `IngestionPipelineRunner` deletes the registered proxies this attempt **created** (never a final file that existed before the attempt, since a proxy path derives from content and another row, active or soft-deleted, may reference it) plus its own temporary files, and removes the directory only if it is left empty — never recursively, since a concurrent attempt on the same content shares it until one of them persists. An attempt that loses a uniqueness race (`LostIngestionRace`: the active content-hash index or the one-proxy-per-asset-and-type index) first releases its paired MOV's registrations, because the winner owns those content-derived files. A clean shutdown is treated differently: the in-flight temporary file is dropped, but proxies already completed in that run are kept, and the consumer loop re-throws the cancellation instead of recording an item failure and draining the queue. Each attempt has its own `IngestionContext.AttemptId`, which names temporary files and scopes compensation; `IngestionContext.AssetId` is the row that owns the proxies, set by classification (the stored row for `ContentChanged`, the attempt id for new content). A hard kill, the storage-config cascade delete, and the old content directory of a file edited in place are out of reach in-process and are reclaimed by an admin through a proxy sweep (see "Proxy sweeps"); the old directory is deliberately not deleted after commit, because a copy of the old content elsewhere can be ingested into it in the same crawl.

### Flashback Interaction Rules

`AssetInteraction` state is per user and shapes that user's flashback queries (Epic 5+):
- `hidden = true` → excluded from the user's flashback queries. Applied **at query level** by `NotHiddenFor(userId)` (#48), not a global filter; other users still see the asset
- `starred = true` → always included; sorted first within its year group
- `liked = true` → `DisplayWeight` 2.0 (otherwise 1.0), used for candidate weighting when a date has many assets across years. `DisplayWeight` is **computed** by `FlashbackWeight.For` in Core, never stored — there is no such column (#46)

## Conventions

- **Enforced by the build** (`TreatWarningsAsErrors`, `src/.editorconfig`, central package management) and by `dotnet format --verify-no-changes`: file-scoped namespaces matching the folder, `camelCase` private fields (static ones too) with no underscore in production code (test projects suppress `IDE1006`), `I`-prefixed interfaces, 4-space indentation with LF line endings, `AnalysisMode=Recommended`, and package versions only in `Directory.Packages.props`. When one fires, fix the code rather than suppress the rule.
- **XML doc comments**: write `/// <summary>` only when it adds information beyond the name. Self-explanatory members (e.g. `DefaultPort`, simple repository methods) go without, overriding any global rule that asks for docs on every public member.

## Multi-User Design

Each user has a private set of `UserStorageConfig` entries pointing to NAS roots. `RootPath` is globally unique — a path cannot be assigned to more than one storage config across all users. `UserStorageConfig → MediaAsset` scopes assets to a user's assigned NAS root. `RootPath` is a **container** path read by both the Worker (crawling) and the API (serving originals), so it must resolve to the same folder in every container: each user's NAS folder is mounted at `/data/originals/<user>` everywhere, and only the host side of the mount varies with the NAS layout. Either one worker per user (`WORKER__USER` + `WORKER__ROOTPATH=/data/originals/<user>`, which has no default and is required with `WORKER__USER`, mounting only that user's folder) or one all-user worker (no `WORKER__USER`, configs added through the admin API). Each user has private `AssetInteraction` state — never shared.

**Media is private per user.** Every media read goes through one visibility rule (`MediaAssetVisibility.VisibleTo(userId)`): an asset is visible only to the owner of its storage config, only while that config is active, and never once soft-deleted — `VisibleTo` checks all three itself, so it holds even under `IgnoreQueryFilters()`. It covers the asset only: related data needs its own filter (`Interactions` to the caller's rows, `Burst` members through `VisibleTo` again). Endpoints map "not visible" to **`404`, never `403`**, so other users' asset ids cannot be probed. Admins get no media access — their powers cover identity and storage configs only.

**Asset lists are read through `IAssetViewReader`** (`Anichron.Core/Data/Repository/AssetViewReader.cs`, #253). It always applies `VisibleTo` and `NotHiddenFor` to the caller and leaves out a Live Photo's paired MOV (any row that a row visible to the caller references through `PairedAssetId`, so hiding the Live Photo does not surface its video; the MOV reaches the client only as the Live Photo's `PairedMovId`, #320, and a soft-deleted MOV not at all), then the caller's `IAssetSelection` (e.g. `OnThisDay`, which filters only on `MediaAsset` columns and also brings every member of a burst with a visible member on the day, or `BurstMembers`, one burst's members), then left-joins the caller's own interaction row, all projected into a flat `AssetView` in one SQL statement. A new list view adds a selection class; it never changes the reader, and no API type touches a media entity (architecture rule `api-reads-media-through-views`). Grouping and ordering happen in memory on the result: `FlashbackYears` collapses each burst to its cover (`Bursts.Cover`: the caller's earliest visible member, starred or liked when any member is, `burst { burstId, isCover, count }`), kept only when the cover falls on the requested day (`OnThisDay.FallsOnTheDay`), then orders years newest first, starred first, then capture time. The published `mediaType` is `"LivePhoto"` only when the DTO carries the live video URL (the paired MOV is visible and has a `WebVideo` proxy), otherwise `"Image"`; the stored `MediaType` stays `LivePhoto`, so a returning video needs no write (`AssetDtoMapper`, #321).

### Work queue

Durable work for the Worker lives in `WorkItems` (#276, ADR 0008). Its kinds are the crawl (`Crawl`) and burst detection (`BurstDetection`), each one per storage config, periodic maintenance (`TokenCleanup`, `WorkItemRetention`), and the admin-requested proxy sweep and deletion (`ProxySweep`, `ProxyDeletion`, #174).

- **Producing:** `IWorkQueue.EnqueueAsync` (`EfWorkQueue`, Core) is the only insert. A payload with a `DedupKey` coalesces: while an item with that key is `Queued`, enqueueing returns it (a partial unique index absorbs racing inserts).
- **Scope:** a scoped item carries the `StorageConfigId` it belongs to (`IWorkPayload.StorageConfigId`). A per-user Worker (`WORKER__USER`) claims unscoped items and items of its user's configs; an all-user Worker claims everything. The served user is `WorkerState.ResolvedUserId`, read at each claim and tick, never cached at construction.
- **Scheduling:** every Worker runs `WorkScheduler`. Each poll (`Worker:WorkPollIntervalSeconds`, 5 s) it recovers expired leases, then asks each `IWorkScheduleSource` for its schedules (a fixed `WorkSchedule<TPayload>` is its own source; `StorageConfigSchedules<TPayload>` returns one per active config the Worker serves, at the crawl interval, for any `IStorageConfigPayload<TSelf>`) and whether each is due: nothing queued or running with its key and nothing finished within its interval. No timer state is kept, so overdue work runs right after downtime.
- **Executing:** `WorkExecutor` claims the oldest queued item (`FOR UPDATE SKIP LOCKED`), one at a time, under a lease (`WorkLeaseSeconds`, 60 s) that a heartbeat (`WorkHeartbeatSeconds`, 2 s) extends while writing the progress snapshot. `UpdatedAt` is the change cursor, so a heartbeat moves it only when the snapshot changed; `LeaseUntil` is what says the Worker is alive. Every write about a running item is fenced by `(Id, Attempt)`. A fenced heartbeat cancels the handler and writes nothing more; a shutdown releases the item without a stall; a handler exception fails it with no retry. An expired lease requeues the item and counts a stall; the `WorkMaxStalls`th (3) fails it as stalled. An item handed back (release or recovery) while another with its key is already queued ends `Cancelled` as superseded, since the queued one does the same work. A Worker claims only the kinds it knows, so a newer kind waits for a newer Worker. `WorkItemRetention` deletes items finished more than `WorkItemRetentionDays` (30) ago.
- **Cancelling:** `POST /api/v1/admin/work-items/{id}/cancel` (#280) goes through `EfWorkItemCanceller`, one status-guarded `UPDATE`: a queued item ends `Cancelled` at once (`200`), a running one gets `CancelRequestedAt` (`202`), a finished one is `409` and an unknown id `404`. The heartbeat reads the request and cancels the handler but keeps beating until it returns; the item then ends `Cancelled` with its last progress snapshot (and a summary, if the handler returned one), shown as partial (`ResultIsPartial`). A handler that fails after the request still fails. A requested item is never queued again: release and lease recovery end it `Cancelled`, even at the stall limit. A cancelled periodic item counts as that run for due-ness, so the next one follows a full interval later; were it not counted, the scheduler would enqueue it again at the next poll and undo the cancel.
- **Admin view (#279):** `GET /api/v1/admin/work-items` (filtered by status and kind, newest request first, paged), `.../{id}`, and `.../events`, a Server-Sent Events stream that re-queries `UpdatedAt` every `WorkItemEvents:IntervalSeconds` (2 s). An event's id is the cursor, and the list carries the cursor read before its page, so a client that lists and then subscribes with it as `Last-Event-ID` misses nothing; an item may arrive twice and is merged by id. The API reads items only through `IWorkItemReader` (Core), as `WorkItemView`. A result is shown once an item succeeded or was cancelled (then marked partial), an error once it failed or was cancelled.
- **Adding a kind:** a `WorkKind` value, a payload record in Core implementing `IWorkPayload` (static `Kind`, optional `DedupKey`), an `IWorkHandler<TPayload>` in the Worker registered with `AddWorkHandler`, and for a periodic kind a `WorkSchedule<TPayload>` subclass registered as an `IWorkScheduleSource` (or a source that builds several; a per-config kind implements `IStorageConfigPayload<TSelf>` and registers `StorageConfigSchedules<TPayload>`). `WorkHandlerRegistry` fails startup unless every kind has exactly one payload and one handler; nothing switches on the kind.
- **Handler contract:** a handler receives its payload and a `WorkRun` (the item's id and its progress sink). Delivery is at least once. Each run starts from scratch and converges, side effects are safe to repeat, and the returned summary becomes `Result`.

### Proxy sweeps

Orphaned proxy files are reported, never deleted unattended (#174). `POST /api/v1/admin/proxy-sweeps` enqueues a `ProxySweep` (unscoped, one at a time); `ProxySweepHandler` walks the proxy root shard by shard and records into `ProxyOrphans` every file that no `ProxyFiles.ProxyPath` references (soft-deleted assets' included, read by `LIKE 'ab/%'` on `ix_proxyfiles_proxypath`) and that was last written more than an hour before the scan, with the storage config its directory names (`IProxyDirectoryStrategy.StorageConfigOf`). Its result, `{ orphans, bytes, temporaryFiles, configsGone }`, is counted from the recorded report, so a retried sweep reports what an earlier attempt recorded too. `GET .../{sweepId}/orphans` pages the report by path, filtered by `outcome`, `pending=true` (no deletion processed it yet; not with `outcome`) and `storageConfigId` (`409` until the sweep succeeded). `POST .../{sweepId}/deletions` with `orphanIds` (at most 1000) or `all` enqueues one `ProxyDeletion` per live config the orphans name, scoped to it, and one unscoped item for orphans of a config that no longer exists or of none; `IProxyOrphanReader` resolves which configs are live in the same query. `ProxyDeletionHandler` re-checks each unprocessed orphan of its scope just before removing it: still unreferenced, still present, same size and last write time (to the microsecond), else `SkippedReferenced`, `Missing` or `SkippedChanged`; a failed delete is `Failed`. It removes an emptied `{configId:N}-{hash}` folder (`IProxyDirectoryStrategy.ContentDirectoryOf`), never a shard folder. Retention of the sweep's work item purges its report.

### Worker Processing Model

Processing uses a bounded-concurrency `Channel<T>` pipeline. The crawler produces file paths; N consumer tasks process them concurrently. Default concurrency: `Worker:MaxConcurrentFiles = 4`. Processing is always idempotent — files already in the DB (matched by `(StorageConfigId, ContentHash)`) skip extraction and proxy generation; only the row's `FilePath`/`LastSeenOnNas` are updated, and a file whose bytes changed at a stored path updates that row in place (see Reconciliation above).

**Classify once, then one handler per outcome (#254).** Right after hashing, `ClassificationMiddleware` asks `IngestionClassifier`, which loads every relevant row in one query (`FindByHashesOrPathsAsync`: any row by either half's hash, any row at either half's path), and gives each half a `FileClassification`: a `FileOutcome` (`New`, `ContentChanged`, `Unchanged`, `Moved`, `DuplicateCopy`, `PathTaken`, `OverwrittenByCopy`, `OverwrittenByMove`, `AutoRestored`, `AutoRestoredMoved`, `AutoRestoredContentChanged`, `LoneLivePhotoVideo`; see Reconciliation above), `Stored` (the row the outcome acts on) and `Retired` (the row it soft-deletes), plus a `PairingAction` for the item (`None`, `AttachMov`, `BackfillMovProxy`). No other step asks the database whether content is known, except `IngestionSaver` checking whether a concurrent consumer's row landed after a lost insert race. A `New`, `ContentChanged` or `AutoRestoredContentChanged` primary continues through EXIF, proxies and `PersistenceMiddleware`, which hands the write to the `IOutcomeWriter` whose `Outcomes` include it (`NewContentWriter` inserts, `ContentChangedWriter` updates the stored row in place; both share `ProcessedContentWriter`, whose `ContentOf` is the only EXIF → `AssetContent` mapping), after `KnownFileReconciler` has reconciled the MOV half. A stored row takes new content only through `StoredContent.TakeAsync` (content fields, `Metadata` through `MetadataValues.Copy`, proxy rows by type, burst mark cleared, reactivated), whether the content was processed (`ContentChanged`, `AutoRestoredContentChanged`) or comes from another row (`OverwrittenByMove`, `AssetContent.Of`); `StoredContent.ReactivateAsync` is the one place a row is auto-restored and its count staged (#327). Every other outcome stops the item after `LivePhotoPairer` and `KnownFileReconciler` have updated the stored rows. Each item stages on its context the rows it soft-deletes and auto-restores, `IngestionSaver` counts them once a save has persisted them (a lost save counts nothing), and the crawl sums them. All saves go through `IngestionSaver`, and unique violations are recognised by index name with `IsUniqueViolation` (`Anichron.Core/Data/UniqueViolations.cs`). A new outcome, as content changed at a known path (#250) was, is a classifier branch, a `FileOutcome` value and its handler or writer; no other middleware changes, except that `ClassificationMiddleware` saves `OverwrittenByMove` through `TrySaveRetiringFirstAsync`, the one outcome whose save must retire a row first. When a Live Photo's HEIC is already stored without its MOV (the halves arrived in different crawls), `LivePhotoPairer` attaches the MOV to the stored row and commits before the halves are reconciled; `PairedMovResolver` reuses the MOV row the classifier found rather than inserting a duplicate. The paired MOV is its own asset and owns its `WebVideo` proxy, written into the MOV's own content-hash directory by `PairedMovProxyWriter` only when that MOV has none yet — on first ingest, on late pairing, and on any later crawl of an already-paired HEIC whose MOV still lacks it (self-heal).

## Infrastructure

- Worker Dockerfile targets `mcr.microsoft.com/dotnet/runtime:10.0-noble` (not Alpine); published as multi-arch (`linux/amd64` + `linux/arm64`). `intel-media-va-driver-non-free` installed on `amd64` only; GPU fallback handles its absence at runtime. OpenCvSharp's native library needs `libatomic1` on `amd64` and `libtesseract5` + `libwebpdemux2` on `arm64` (ADR 0009).
- API Dockerfile uses Alpine (no GPU/FFmpeg needed)
- CI/CD runs on GitHub Actions (`.github/workflows/ci.yml`); API and Worker images built in parallel; pushed only after `CI gate` passes, tagged `sha-<hash>` on every master build, `edge` on master, SemVer tags on releases
- Branch protection requires only `CI gate` and `CodeQL`. To make a new job required, add it to the gate's `needs`, never to branch protection. The gate's rule is `scripts/ci-gate.sh`
- PostgreSQL connection is never hardcoded — always read from `POSTGRES_CONNECTION__*` env vars or Docker secrets
- CORS allowed origins configured via `CORS__ALLOWEDORIGINS` env var (comma-separated); the same value switches the refresh cookie to `SameSite=None`. Leave empty for same-origin / reverse proxy deployments.
- `X-Forwarded-For` is honoured only from loopback or from addresses listed in `FORWARDEDHEADERS__KNOWNPROXIES` / `FORWARDEDHEADERS__KNOWNNETWORKS` (comma-separated IPs / CIDRs; `ForwardLimit = 1`). Rate limiting partitions on the resulting client IP, so a reverse-proxy deployment must set one of these or all clients share the proxy's bucket. Never trust the Docker bridge network for a directly published port — that lets any client spoof its IP.
- Registration requires an admin-issued invite token. The first user (bootstrap admin) is exempt.
- Observability: `GET /api/v1/healthz` (database, proxy storage, originals storage; a missing mount reports Degraded). Logging is the default .NET console logger with `LoggerMessage` source-generated messages (architecture rule `logging-is-source-generated`; `BeginScope` is allowed); structured JSON output and OpenTelemetry are **not** wired up.
- License: AGPL-3.0.

## GitHub References

- **Issues** (bug reports, feature requests, epic tracking): https://github.com/bitbiter-dev/anichron/issues
- **Wiki** (early architecture decisions, e.g. ADR-3 on flashback weighting, which older issues link to): https://github.com/bitbiter-dev/anichron/wiki. New ADRs go in `docs/adr/`.
- **What to work on next**: the v1.0 order lives on GitHub, not here — each epic milestone's description lists its sequence, and each issue carries native *blocked by* links. An open "Architecture review due" issue goes before anything else (procedure: `docs/agents/design-process.md`, "Architecture review"). Otherwise pick an open `ready-for-agent` issue with no open blocker. A `needs-design` issue is picked only to write or approve its design note; a `needs-decision` issue waits for the maintainer.

## Code graph (who uses a symbol)

To find what calls, references or implements a C# symbol, ask the graph before grepping:

```bash
scripts/graph.sh affected <Symbol>   # everything that depends on it, tests included (depth 2, file:line)
scripts/graph.sh explain <Symbol>    # its direct callers and callees
```

It builds or refreshes the local, gitignored `graphify-out/` itself (seconds, no LLM). Use symbol names; free-text `graphify query` and `GRAPH_REPORT.md` are noisy and not worth the tokens. Grep stays the tool for text, config and anything that is not a symbol.

## Engineering process (objection, first caller, design note)

Agents write issues, designs and code; the maintainer sets requirements in conversation and makes decisions. Before writing an issue, designing, implementing or reviewing, read `docs/agents/design-process.md`. Its three load-bearing terms:

- **Objection:** when the requirement, design or spec is wrong for the system, object on the issue, relabel it and stop.
- **First caller:** every building block ships with the code that first uses it.
- **Design note:** issues that change the system's shape get one, approved before `ready-for-agent`.

## Agent skills

### Issue tracker

Issues live as GitHub issues in `bitbiter-dev/anichron`, driven via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage roles, each label string equal to its name, plus the repo's `needs-design` and `needs-decision`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — one `GLOSSARY.md` (not created yet; written lazily by `/domain-modeling`) plus `docs/adr/` at the repo root. See `docs/agents/domain.md`.
