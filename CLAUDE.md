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
# Run all tests
dotnet test

# Run tests for a specific project
dotnet test src/SomeProject.Tests.Unit/SomeProject.Tests.Unit.csproj

# Run a single test
dotnet test --filter "FullyQualifiedName=Namespace.ClassName.MethodName"
```

Test projects must be named `*.Tests.Unit` — `Directory.Build.props` auto-applies the test SDK to any project matching that pattern: xUnit v3, **NSubstitute** (not Moq), FluentAssertions, `System.IO.Abstractions.TestingHelpers`, and coverlet. Internal members are exposed to test projects via `InternalsVisibleTo`.

### Mutation testing

```bash
dotnet tool restore
scripts/mutation-changed.sh   # scoped: only C# files changed since origin/master (~2 min)
cd src && TMPDIR="$(mktemp -d)" dotnet stryker   # full sweep (~8 min); must run from src/, not the repo root
```

⚠️ **Never run `dotnet stryker` with the shared `TMPDIR`.** Stryker's MTP runner passes the active mutant through `$TMPDIR/stryker-mutant-<n>.txt`, a path every run on the machine shares, so two agents mutating at the same time corrupt each other's results (survivors that tests do kill, scores down to 0%). `mutation-changed.sh` isolates itself; give a direct run its own `TMPDIR` as above. See `docs/mutation-testing.md`.

All settings live in `src/stryker-config.json` — pass no flags that change **what gets measured**, so that a local run and the CI run stay comparable. **Two deliberate exceptions, and only two**: `.github/workflows/mutation-divergence.yml` passes `--concurrency`, because concurrency is the variable that job exists to test; and `scripts/mutation-changed.sh` passes `--mutate` to scope a local pre-check to the changed files, whose score covers only those files and is never compared with the CI score. Anywhere else, a measurement-affecting flag is a bug. **Never use `--since` or `--with-baseline`**: inside a git worktree they diff the main checkout instead of the worktree, test 0 mutants and exit `0` — a false green.

Threshold flags are a separate category and are *not* covered by that rule: `--break-at`, `--threshold-low` and `--threshold-high` change the **verdict**, not the measurement, so passing them does not make two runs incomparable. `ci.yml` passes all three, computed by `scripts/mutation-ratchet.sh` from the stored high-water mark. They are always passed **together** — Stryker validates `break <= low <= high` before mutating and exits `1` if it fails, which reads as a broken tool rather than a threshold event. Never raise one by hand without the others.

**Never switch `test-runner` away from `mtp`**: the default `vstest` runner cannot observe xUnit v3 failures and silently reports `0.00%`. See `docs/mutation-testing.md` and `docs/adr/0001-mutation-testing-on-the-mtp-runner.md`.

#### Surviving mutants: test first, exclude only when equivalent

The gate is **100%**: one surviving or uncovered mutant fails CI. Before finishing any change to production code, run `scripts/mutation-changed.sh` and work through every undetected mutant in the files you touched. It maps a changed `<Name>Tests.cs` to `<Name>.cs` and exits `1` for a changed test file it cannot map; pass the source files that test covers, relative to `src/`, as arguments. The full sweep in CI is the authority; run `dotnet stryker` locally as well only when the change reaches beyond the files it touches (`stryker-config.json`, a shared base class, test doubles):

1. **Write a test.** A survivor is a missing test until proven otherwise. The test asserts behaviour that the mutant changes, with an expected value from an independent source (a literal, the spec), never recomputed the way the code does.
2. **Question an exclusion only for an *equivalent* mutant**: one where no input can make the original and the mutant behave differently. Prove it by naming why every input agrees (a square image, a value exactly at the bound, a branch that only logs), or by measuring it. "Hard to test" is not equivalence.
3. **Prefer a rewrite that removes the equivalent branch** without changing behaviour (a clamp instead of a guard, an early return instead of a logging-only `if`). Suppress only when no such rewrite exists.
4. **Suppress narrowly.** Add a bare `// Stryker disable once <Mutator>` before the statement, and a row in ADR 0005 saying why. The directive binds to the next syntax node and disables *every* mutation of that mutator on it, so first confirm no killable sibling mutation on the same node gets hidden; if one would, restructure instead. A whole-file exclusion is only for pure wiring with no branching logic, listed by path in `stryker-config.json` and in ADR 0005.
5. **Code that only real PostgreSQL can exercise** (transactions, `ExecuteUpdate`/`ExecuteDelete`) gets a per-member `// Stryker disable all` and an ADR 0005 row. It is not tested on SQLite or another stand-in engine.

Done when the scoped run reports `100.00%` (or nothing to mutate) and every new directive or exclusion has its ADR 0005 row. Lowering the thresholds or `SLACK` is never the fix; the reason is in `docs/mutation-testing.md`, "Why there is no slack".

Mutants that flip between runs come from `static readonly` initialisers and EF's cached model: build such data in a property or method, and build model-asserting test contexts with `EnableServiceProviderCaching(false)`. An assertion behind `?.` (`x?.Value.Should()...`) never runs when `x` is null; assert presence first (`ContainSingle(...).Which`). Rationale for every existing exclusion: `docs/adr/0005-mutation-scope-excludes-unkillable-and-wiring-code.md`.

## Architecture

Four projects with strict separation of concerns:

| Project | Role |
|---------|------|
| `Anichron.Core` | Single source of truth: domain models, `AnichronDbContext`, all Fluent API config, shared utilities (XXHash64, path parsing). Zero dependencies on other projects. |
| `Anichron.Infrastructure` | Shared host plumbing used by API and Worker: configuration (`DatabaseConfiguration` reads the connection from `POSTGRES_CONNECTION__*` env vars or Docker secrets; INI bootstrap; `ConfigurationList`), migration under a PostgreSQL advisory lock, and the API's `ReadOnlyFileSystem`. Each host wires its own DI. |
| `Anichron.API` | ASP.NET Core Minimal APIs — owns all identity/auth/admin writes (users, storage configs, refresh tokens, invites, lockout); read-only for media data. Media endpoints (DTOs #242, proxy serving #43, originals #44, On This Day #38) are not built yet. All routes prefixed `/api/v1/`. |
| `Anichron.Worker` | `BackgroundService`s — NAS crawling, EXIF extraction, FFmpeg transcoding, proxy generation, expired-refresh-token cleanup; reconciliation is planned (Epic 4) |

**Write ownership is split by domain, not centralized.**
- **Worker is the only writer of *media* data** — `MediaAsset`, `Metadata`, `ProxyFile`, `Burst`, plus all reconciliation and soft-deletes happen here. The API never writes media. Its one identity-side write is `TokenCleanupService` deleting expired refresh tokens.
- **API owns all *identity/auth/admin* writes** — registration, login/refresh-token issuance and revocation, lockout state, invites, and admin user/storage-config CRUD all persist through API services (`AuthService`, `AdminUserService`, `AdminStorageConfigService`, `TokenService`, etc.) via `IUnitOfWork.SaveChangesAsync`. The API is read-only *only with respect to media*. The one exception is `AssetInteraction` (star/like/hide, #46): per-user state, to be written by the API.
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

**Nine entities**, all EF Core config via **Fluent API only**, no data annotations. Columns are
**PascalCase** — the only explicitly-named column in the whole model is `Invites.xmin`. Table names
do not always match entity names (`AssetInteraction` → `Interactions`, `UserStorageConfig` →
`StorageConfigs`):

- `MediaAsset` — central entity: `FilePath`, `ContentHash` (XXHash64), `Month`/`Day`/`Year` integers, `MediaType`, `IsSoftDeleted`, and a `PairedAssetId` self-reference (HEIC parent → MOV child)
- `Metadata` — 1:1 with `MediaAsset`; EXIF data (dimensions, GPS, raw EXIF orientation tag 1–8, camera make/model/lens, video duration)
- `ProxyFile` — one row per generated artefact, each a file at `ProxyPath` on the local SSD. `ProxyType` is `Thumbnail`, `WebVideo`, `FullPreview` or `BlurHash` — note **BlurHash is a proxy row like any other**, not a string column on the asset
- `Burst` — groups rapid-fire photos with a `PrimaryAssetId` cover; prevents duplicate memories in flashbacks
- `AssetInteraction` — per-user state (starred, liked, hidden, view count, last viewed); unique on `(UserId, AssetId)`
- `UserStorageConfig` — NAS root paths per user; `RootPath` is **globally unique** — a path cannot be assigned to more than one storage config across all users. Paths are stored canonical (absolute, `.`/`..`/`//` collapsed, no trailing slash) via `StoragePath.Canonicalize`, so `/p` and `/p/` cannot both be assigned. A `RootPath` must be a folder strictly inside `StoragePath.OriginalsRoot` (`/data/originals`), never the root itself, and may not contain or be contained by another config's root (`StoragePath.Overlaps`) — nested roots would index the same files twice
- `User` — owns storage configs and interactions
- `RefreshToken` — issued and revoked by the API, expired rows deleted by the Worker's `TokenCleanupService`; `TokenHash` unique
- `Invite` — admin-issued registration tokens; `TokenHash` unique, and the only entity carrying an optimistic-concurrency token (`xmin`)

### Key EF Core / Database Decisions

- `Month` and `Day` stored as separate integers on `MediaAsset` with a **composite index** (`IX_MediaAsset_Flashback`) — enables "On This Day" queries as direct index lookups instead of `EXTRACT()` scans
- Partial unique index on `(StorageConfigId, ContentHash)` filtered to `"IsSoftDeleted" = false` (`MediaAssetIndexNames.ActiveContentHashUnique`) — enforces one *active* asset per content within a storage config; it is what lets a moved file be re-linked to its existing row by hash (#35). Dedup is **config-scoped**: the same bytes under two storage configs are two assets, which is what makes multi-user work. A *global* unique index on `ContentHash` alone would break that and must not be added. When two consumers race on the same content, the loser's insert hits this index and `PersistenceMiddleware` skips the file without throwing, so pipeline compensation never deletes the winner's shared proxies
- Unique index on `(StorageConfigId, FilePath)` — one asset row per path within a config, soft-deleted rows included
- Unique index on `ProxyFiles (AssetId, ProxyType)` (`ProxyFileIndexNames.OnePerAssetAndType`) — at most one proxy of each kind per asset, so two attempts racing to add the same proxy (e.g. a paired MOV's backfilled `WebVideo`) cannot both land; the loser is a `LostIngestionRace`
- Filtered index `IX_MediaAssets_Active` on `IsSoftDeleted`, filtered to `"IsSoftDeleted" = false` — excludes trashed assets from active gallery queries
- Global query filters hide soft-deleted assets: on `MediaAsset`, and on `AssetInteraction` and `Burst` through their asset. This is the **only** global filter — per-user rules (hidden, ownership) are query-level, because the Worker shares `AnichronDbContext` and has no user
- `User.Username` and `User.Email` carry an EF value converter (`IdentityNormalizer.Normalize`: trim + lowercase) that applies to every write **and** to every equality comparison, so repository lookups need no manual normalization. ⚠️ The InMemory provider does not convert query parameters, so read-side behaviour is tested via `ToQueryString()` on the Npgsql provider, not InMemory
- `MediaType` and `ProxyType` stored as strings (not integers)
- **Never use `DateTime`** — NodaTime is used exclusively for all date/time values

### Storage Layout

```
/data/originals/   ← NAS (mounted :ro by both API and Worker — originals are never modified)
  <user>/          ← one mount per user, at the SAME container path in the API and that user's worker
/data/proxies/     ← Local SSD (Worker writes; API reads for serving) — fixed StoragePath.ProxiesRoot, not a setting, so both always agree
```

Proxy files follow a two-level shard path derived from **content, not from the ingestion attempt**: `/data/proxies/{content_hash[0:2]}/{storage_config_id:N}-{content_hash}/{type}` (e.g. `01/3fa85f6457174562b3fc2c963f66afa6-0123456789abcdef/thumbnail.jpg`). Re-ingesting the same file therefore resolves to the same directory, so a retry overwrites its own debris instead of stranding it; the storage config id keeps byte-identical files of two users apart, without which removing one user's config would cascade away the other's proxies. `IProxyDirectoryStrategy` is the single place this rule lives. The API serves them at `GET /api/v1/assets/{id}/proxies/{thumbnail|preview|video}` (visibility rule first, then only the stored `ProxyPath` under the proxy root; strong `ETag`, `private, immutable` caching, range requests). `ProxyFile.ProxyPath` stores the path relative to `/data/proxies/` (note: `FilePath` is on `MediaAsset`, relative to the storage config root — the two are different properties).

The API's DI-registered `IFileSystem` is a `ReadOnlyFileSystem` (`Anichron.Infrastructure/FileSystem/`): writes throw `NotSupportedException`, and streams open only with `FileMode.Open` and no write access. Read-only streams, `Directory.Exists` and the write-free components (`Path`, `DriveInfo`, …) are forwarded — any other read throws `NotImplementedException`, which means "add the forwarding override and a test", not "use `new FileSystem()`". This includes the `FileInfo`/`DirectoryInfo` factories, because their objects can write.

### Worker Media Processing

- Crawls NAS on a configurable interval (`Worker:CrawlIntervalHours`, default 4 h; immediate crawl on startup)
- The crawl walks folder by folder and never enters `CrawlExclusions`: any dot-folder or dot-file (macOS/Linux/QNAP/ZFS/sync-tool metadata), NAS and OS system folders (`@eaDir`, `#recycle`, `@Recycle`, `$RECYCLE.BIN`, `lost+found`, Nextcloud `files_trashbin`/`files_versions`, …) and app library packages (`*.photoslibrary`, `*.lrdata`, …, which hold thumbnails and renders). `Worker:ExcludedDirectories` adds comma-separated folder names (not paths; rejected at startup) on top. An unreadable subfolder is logged and skipped; an unreadable or missing root still fails the crawl
- Computes XXHash64 `ContentHash` → extracts EXIF via MetadataExtractor → writes `MediaAsset` + `Metadata`
- Generates proxy types: `Thumbnail`, `FullPreview`, `WebVideo`, `BlurHash` (the `ProxyType` enum, persisted as text)
- Image proxies are auto-oriented once after decoding: upright pixels, orientation tag 1. `Metadata.Orientation` applies only when serving originals — never rotate a proxy by it
- Image proxy sizes are edge-based, so portrait and landscape get the same treatment: preview long edge `PreviewLongEdge` (1920), thumbnail short edge `ThumbnailShortEdge` (300), blurhash sample long edge `BlurhashSampleLongEdge` (64). Nothing is ever upscaled
- Each `IImageProxyGenerator` declares a `ProxyBound`; `ImageProxyMiddleware` decodes once to `Rgb24`, shrinks the shared image to each bound largest-first, and hands every generator **its own copy** at that size. Generators may crop/recolour/resize their copy freely; only the middleware resizes the shared image
- FFmpeg transcodes video with runtime GPU detection: QuickSync (`h264_qsv`) → NVENC (`h264_nvenc`) → AMF (`h264_amf`) → software (`libx264`)
- Burst detection (**planned, not yet implemented**): will group rapid-fire sequences and assign a `PrimaryAssetId` cover. The `Burst` entity and schema exist; no detection code is wired into the Worker yet.
- Reconciliation (**planned, Epic 4**): a periodic scan soft-deletes assets whose file is gone (interactions survive) and restores them when it reappears (#33, #34, #37). Until #35 lands, a moved or renamed file keeps its **old** `FilePath` (the hash match skips it) and `LastSeenOnNas` is written only at first ingest
- Failure ownership: `ProxyStagingWriter` is the single place a proxy reaches its final name — it writes under a per-attempt `.<attempt id>.tmp` suffix (so concurrent attempts on the same content never share a temporary file), renames into place once complete, and registers the proxy on the `IngestionContext` at that moment; the proxy middlewares only supply the bytes. If a step throws, `IngestionPipelineRunner` deletes exactly those registered proxies plus its own temporary files, and removes the directory only if it is left empty — never recursively, since a concurrent attempt on the same content shares it until one of them persists. An attempt that loses a uniqueness race (`LostIngestionRace`: the active content-hash index or the one-proxy-per-asset-and-type index) first releases its paired MOV's registrations, because the winner owns those content-derived files. A clean shutdown is treated differently: the in-flight temporary file is dropped, but proxies already completed in that run are kept, and the consumer loop re-throws the cancellation instead of recording an item failure and draining the queue. A hard kill or the storage-config cascade delete is out of reach in-process and belongs to the orphan sweeper (#174).

### Flashback Interaction Rules

`AssetInteraction` state is per user and shapes that user's flashback queries (Epic 5+):
- `hidden = true` → excluded from the user's flashback queries. Applied **at query level** through one shared composition (#48), not a global filter; other users still see the asset
- `starred = true` → always included; sorted first within its year group
- `liked = true` → `DisplayWeight` 2.0 (otherwise 1.0), used for candidate weighting when a date has many assets across years. `DisplayWeight` is **computed**, never stored — there is no such column (#46)

## Conventions

- **Namespaces**: File-scoped, matching `{AssemblyName}.{Layer}` (enforced as warning in `.editorconfig`)
- **Nullability**: Enabled globally — treat as baseline
- **Implicit usings**: Enabled globally — no need to import common `System.*` namespaces
- **Private fields**: plain `camelCase` for instance — **no underscore prefix**; `PascalCase` for static
- **Interfaces**: Prefixed with `I`
- **Indentation**: 4 spaces, **LF** line endings (`src/.editorconfig` sets `end_of_line = lf`)
- **Analyzers**: `AnalysisMode=Recommended` with `TreatWarningsAsErrors`, so every analyzer warning fails the build — fix the code rather than suppress the rule
- **NuGet versions**: Centralized in `Directory.Packages.props` — never specify a version in individual `.csproj` files
- **XML doc comments**: Do not add `/// <summary>` unless it meaningfully adds information beyond what the name already communicates. Self-explanatory members (e.g. `DefaultPort`, simple repository methods) do not need them. Override the global rule requiring docs on every public member.

## Multi-User Design

Each user has a private set of `UserStorageConfig` entries pointing to NAS roots. `RootPath` is globally unique — a path cannot be assigned to more than one storage config across all users. `UserStorageConfig → MediaAsset` scopes assets to a user's assigned NAS root. `RootPath` is a **container** path read by both the Worker (crawling) and the API (serving originals), so it must resolve to the same folder in every container: each user's NAS folder is mounted at `/data/originals/<user>` everywhere, and only the host side of the mount varies with the NAS layout. Either one worker per user (`WORKER__USER` + `WORKER__ROOTPATH=/data/originals/<user>`, which has no default and is required with `WORKER__USER`, mounting only that user's folder) or one all-user worker (no `WORKER__USER`, configs added through the admin API). Each user has private `AssetInteraction` state — never shared.

**Media is private per user.** Every media read goes through one visibility rule (`MediaAssetVisibility.VisibleTo(userId)` for queries, `IMediaAssetReadRepository.FindVisibleAsync` for a single asset): an asset is visible only to the owner of its storage config, only while that config is active, and never once soft-deleted — `VisibleTo` checks all three itself, so it holds even under `IgnoreQueryFilters()`. It covers the asset only: related data needs its own filter (`Interactions` to the caller's rows, `Burst` members through `VisibleTo` again). Endpoints map "not visible" to **`404`, never `403`**, so other users' asset ids cannot be probed. Admins get no media access — their powers cover identity and storage configs only.

### Worker Processing Model

Processing uses a bounded-concurrency `Channel<T>` pipeline. The crawler produces file paths; N consumer tasks process them concurrently. Default concurrency: `Worker:MaxConcurrentFiles = 4`. Processing is always idempotent — files already in the DB (matched by `(StorageConfigId, ContentHash)`) are skipped, including moved files until #35 re-links them. One exception: when a Live Photo's HEIC is already stored without its MOV (the halves arrived in different crawls), `LivePhotoPairingMiddleware` attaches the MOV to the stored row instead of skipping; `PairedMovResolver` reuses an already-stored MOV rather than inserting a duplicate. The paired MOV is its own asset and owns its `WebVideo` proxy, written into the MOV's own content-hash directory by `PairedMovProxyWriter` only when that MOV has none yet — on first ingest, on late pairing, and on any later crawl of an already-paired HEIC whose MOV still lacks it (self-heal).

## Infrastructure

- Worker Dockerfile targets `mcr.microsoft.com/dotnet/runtime:10.0-noble` (not Alpine); published as multi-arch (`linux/amd64` + `linux/arm64`). `intel-media-va-driver-non-free` installed on `amd64` only; GPU fallback handles its absence at runtime.
- API Dockerfile uses Alpine (no GPU/FFmpeg needed)
- CI/CD runs on GitHub Actions (`.github/workflows/ci.yml`); API and Worker images built in parallel; pushed only after `CI gate` passes, tagged `sha-<hash>` on every master build, `edge` on master, SemVer tags on releases
- Branch protection requires only `CI gate` and `CodeQL`. To make a new job required, add it to the gate's `needs`, never to branch protection. The gate's rule is `scripts/ci-gate.sh`
- PostgreSQL connection is never hardcoded — always read from `POSTGRES_CONNECTION__*` env vars or Docker secrets
- CORS allowed origins configured via `CORS__ALLOWEDORIGINS` env var (comma-separated); the same value switches the refresh cookie to `SameSite=None`. Leave empty for same-origin / reverse proxy deployments.
- `X-Forwarded-For` is honoured only from loopback or from addresses listed in `FORWARDEDHEADERS__KNOWNPROXIES` / `FORWARDEDHEADERS__KNOWNNETWORKS` (comma-separated IPs / CIDRs; `ForwardLimit = 1`). Rate limiting partitions on the resulting client IP, so a reverse-proxy deployment must set one of these or all clients share the proxy's bucket. Never trust the Docker bridge network for a directly published port — that lets any client spoof its IP.
- Registration requires an admin-issued invite token. The first user (bootstrap admin) is exempt.
- Observability: `GET /api/v1/healthz` (database, proxy storage, originals storage; a missing mount reports Degraded). Logging is the default .NET console logger with `LoggerMessage` source-generated messages; structured JSON output and OpenTelemetry are **not** wired up.
- License: AGPL-3.0.

## GitHub References

- **Issues** (bug reports, feature requests, epic tracking): https://github.com/bitbiter-dev/anichron/issues
- **Wiki** (early architecture decisions, e.g. ADR-3 on flashback weighting, which older issues link to): https://github.com/bitbiter-dev/anichron/wiki. New ADRs go in `docs/adr/`.
- **What to work on next**: the v1.0 order lives on GitHub, not here — each epic milestone's description lists its sequence, and each issue carries native *blocked by* links. Pick an open `ready-for-agent` issue with no open blocker.

## graphify (knowledge graph — orient here first)

This repo ships a pre-built knowledge graph in `graphify-out/` so agents can orient
themselves without grepping the whole tree. It captures call relationships, shared data,
community structure, and the architecture/CI/domain concepts extracted from the docs.

**Orient before exploring.** For any "how does X work / what calls Y / where is Z handled"
question, query the graph first instead of grepping source blindly:

```bash
graphify query "<question>"      # scoped subgraph answer (much smaller than raw grep)
graphify path "<A>" "<B>"        # shortest relationship path between two concepts
graphify explain "<concept>"     # focused explanation of one node and its neighbours
```

- Read `graphify-out/GRAPH_REPORT.md` for a one-page map: god nodes (most-connected
  abstractions), the ~25 named communities (Auth Service, Worker pipeline, EXIF Extraction,
  Image/Video Proxy middleware, repositories, etc.), and surprising cross-file links.
- Fall back to raw grep/Read only after the graph has oriented you, or to edit/debug
  specific lines.

**Keep it current — enforced before push.** The `pre-push` Husky group runs a
`graphify-update` task that regenerates the graph and **fails the push if committed
`graphify-out/graph.json` is stale**, so reviewers and future agents always inherit an
accurate map. When it blocks you, commit the regenerated files and push again:

```bash
graphify update .                # AST-only re-extraction of changed files, no API cost
git add graphify-out/graph.json graphify-out/GRAPH_REPORT.md && git commit
```

The check skips silently if `graphify` isn't installed (it's an optional dev aid, not part
of the dotnet toolchain). Doc-only changes need a manual `/graphify --update`. Only
`graph.json` (date-stable) and `GRAPH_REPORT.md` are committed; `graph.html`, `cache/`,
`cost.json`, and `manifest.json` are gitignored (volatile or machine-specific). Without a
local `manifest.json`, `graphify update` rebuilds from scratch (AST-only, cheap), so a fresh
clone still produces a correct graph.

## Agent skills

### Issue tracker

Issues live as GitHub issues in `bitbiter-dev/anichron`, driven via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage roles, each label string equal to its name. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — one `CONTEXT.md` (not created yet; written lazily by `/domain-modeling`) plus `docs/adr/` at the repo root. See `docs/agents/domain.md`.
