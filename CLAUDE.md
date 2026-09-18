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

Test projects must be named `*.Tests.Unit` — `Directory.Build.props` auto-applies the xUnit + Moq SDK to any project matching that pattern. Internal members are exposed to test projects via `InternalsVisibleTo`.

## Architecture

Four projects with strict separation of concerns:

| Project | Role |
|---------|------|
| `Anichron.Core` | Single source of truth: domain models, `AnichronDbContext`, all Fluent API config, shared utilities (XXHash64, path parsing). Zero dependencies on other projects. |
| `Anichron.Infrastructure` | DI wiring; reads DB connection from `POSTGRES_CONNECTION__*` env vars or Docker secrets |
| `Anichron.API` | ASP.NET Core Minimal APIs — owns all identity/auth/admin writes (users, storage configs, refresh tokens, invites, lockout); read-only for media data; DTO mapping, proxy file serving, originals on demand. All routes prefixed `/api/v1/`. |
| `Anichron.Worker` | `BackgroundService` — NAS crawling, EXIF extraction, FFmpeg transcoding, proxy generation, reconciliation |

**Write ownership is split by domain, not centralized.**
- **Worker is the only writer of *media* data** — `MediaAsset`, `Metadata`, `ProxyFile`, `Burst`, plus all reconciliation and soft-deletes happen here. The API never writes media.
- **API owns all *identity/auth/admin* writes** — registration, login/refresh-token issuance and cleanup, lockout state, invites, and admin user/storage-config CRUD all persist through API services (`AuthService`, `AdminUserService`, `AdminStorageConfigService`, `TokenService`, etc.) via `IUnitOfWork.SaveChangesAsync`. The API is read-only *only with respect to media*.

A planned `Anichron.UI` (NextJS/React) for the Instagram Story-style flashback experience is documented but not yet in the solution.

### Domain Model (`Anichron.Core/Domain/`)

Seven entities — all EF Core config via **Fluent API only**, no data annotations:

- `MediaAsset` — central entity: file path, `content_hash` (XXHash64), `month`/`day`/`year` integers, media type, soft-delete flag, `live_photo_pair_id` self-reference (HEIC parent → MOV child)
- `Metadata` — 1:1 with `MediaAsset`; EXIF data (dimensions, GPS, orientation degrees, camera make/model, video duration, color space)
- `ProxyFile` — generated web-optimized assets (thumbnail, preview, 720p H.264, blurhash string); stored on local SSD
- `Burst` — groups rapid-fire photos with a `primary_asset_id` cover; prevents duplicate memories in flashbacks
- `AssetInteraction` — per-user state (starred, liked, hidden, view count, last viewed); unique on `(user_id, asset_id)`
- `UserStorageConfig` — NAS root paths per user; `root_path` is **globally unique** — a path cannot be assigned to more than one storage config across all users
- `User` — owns storage configs and interactions

### Key EF Core / Database Decisions

- `month` and `day` stored as separate integers on `MediaAsset` with a **composite index** — enables "On This Day" queries as direct index lookups instead of `EXTRACT()` scans
- Unique B-Tree index on `content_hash` — detects renames/moves without breaking references
- Filtered index on `is_soft_deleted` — excludes trashed assets from active gallery queries
- Global query filter: soft-deleted `MediaAsset` records are hidden by default
- `MediaType` and `ProxyType` stored as strings (not integers)
- **Never use `DateTime`** — NodaTime is used exclusively for all date/time values

### Storage Layout

```
/data/originals/   ← NAS (mounted :ro by both API and Worker — originals are never modified)
/data/proxies/     ← Local SSD (Worker writes; API reads for serving)
```

Proxy files follow a two-level shard path: `/data/proxies/{id[0:2]}/{id[2:]}/{type}` (e.g., `f3/a1b2c4.../thumbnail.jpg`). `ProxyFile.FilePath` stores the path relative to `/data/proxies/`.

### Worker Media Processing

- Crawls NAS on a configurable interval (`Worker:CrawlIntervalHours`, default 4 h; immediate crawl on startup)
- Extracts EXIF via ExifLib/ImageSharp → computes XXHash64 `content_hash` → writes `MediaAsset` + `Metadata`
- Generates proxy types: `thumbnail`, `preview`, `video_720p`, `blurhash`
- FFmpeg transcodes video with runtime GPU detection: QuickSync (`h264_qsv`) → NVENC (`h264_nvenc`) → AMF (`h264_amf`) → software (`libx264`)
- Burst detection (**planned, not yet implemented**): will group rapid-fire sequences and assign a `primary_asset_id` cover. The `Burst` entity and schema exist; no detection code is wired into the Worker yet.
- Reconciliation: periodic NAS scan; soft-deletes missing files (preserves user interactions); hash match re-links moved files

### Flashback Interaction Rules

`AssetInteraction` state affects flashback queries (Epic 5+):
- `hidden = true` → excluded from all flashback queries (global filter, same layer as soft-delete)
- `starred = true` → always included; sorted first within its year group
- `liked = true` → sets `DisplayWeight = 2.0`; used for candidate weighting when a date has many assets across years

## Conventions

- **Namespaces**: File-scoped, matching `{AssemblyName}.{Layer}` (enforced as warning in `.editorconfig`)
- **Nullability**: Enabled globally — treat as baseline
- **Implicit usings**: Enabled globally — no need to import common `System.*` namespaces
- **Private fields**: `_camelCase` for instance, `PascalCase` for static
- **Interfaces**: Prefixed with `I`
- **Indentation**: 4 spaces, CRLF line endings
- **Analyzers**: Run at `Recommended` severity during build — fix warnings, don't suppress
- **NuGet versions**: Centralized in `Directory.Packages.props` — never specify a version in individual `.csproj` files
- **XML doc comments**: Do not add `/// <summary>` unless it meaningfully adds information beyond what the name already communicates. Self-explanatory members (e.g. `DefaultPort`, simple repository methods) do not need them. Override the global rule requiring docs on every public member.

## Multi-User Design

Each user has a private set of `UserStorageConfig` entries pointing to NAS roots. `root_path` is globally unique — a path cannot be assigned to more than one storage config across all users. `UserStorageConfig → MediaAsset` scopes assets to a user's assigned NAS root. Multiple worker instances can each monitor a separate config. Each user has private `AssetInteraction` state — never shared.

### Worker Processing Model

Processing uses a bounded-concurrency `Channel<T>` pipeline. The crawler produces file paths; N consumer tasks process them concurrently. Default concurrency: `Worker:MaxConcurrentFiles = 4`. Processing is always idempotent — files already in the DB (matched by `content_hash`) are skipped.

## Infrastructure

- Worker Dockerfile targets `mcr.microsoft.com/dotnet/runtime:10.0-noble` (not Alpine); published as multi-arch (`linux/amd64` + `linux/arm64`). `intel-media-va-driver` installed on `amd64` only; GPU fallback handles its absence at runtime.
- API Dockerfile uses Alpine (no GPU/FFmpeg needed)
- CI/CD runs on GitHub Actions (`.github/workflows/ci.yml`); API and Worker images built in parallel; tagged `sha-<hash>` on every build, `edge` on master, SemVer tags on releases
- PostgreSQL connection is never hardcoded — always read from `POSTGRES_CONNECTION__*` env vars or Docker secrets
- CORS allowed origins configured via `CORS__ALLOWED_ORIGINS` env var (comma-separated). Leave empty for same-origin / reverse proxy deployments.
- Registration requires an admin-issued invite token. The first user (bootstrap admin) is exempt.
- Observability: structured JSON logging in production; `GET /api/v1/healthz` health endpoint; OpenTelemetry instrumented but no exporter configured by default (`OTEL_EXPORTER_OTLP_ENDPOINT` to enable).
- License: AGPL-3.0.

## GitHub References

- **Issues** (bug reports, feature requests, epic tracking): https://github.com/bitbiter-dev/anichron/issues
- **Wiki** (architecture decisions, ADRs, engineering notes): https://github.com/bitbiter-dev/anichron/wiki

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
