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
| `Anichron.API` | ASP.NET Core Minimal APIs — read-only queries, DTO mapping, proxy file serving, originals on demand |
| `Anichron.Worker` | `BackgroundService` — NAS crawling, EXIF extraction, FFmpeg transcoding, proxy generation, reconciliation |

**Worker is the only database writer.** All inserts, reconciliation, burst detection, and soft-deletes happen here. The API is read-only.

A planned `Anichron.UI` (NextJS/React) for the Instagram Story-style flashback experience is documented but not yet in the solution.

### Domain Model (`Anichron.Core/Domain/`)

Seven entities — all EF Core config via **Fluent API only**, no data annotations:

- `MediaAsset` — central entity: file path, `content_hash` (XXHash64), `month`/`day`/`year` integers, media type, soft-delete flag, `live_photo_pair_id` self-reference (HEIC parent → MOV child)
- `Metadata` — 1:1 with `MediaAsset`; EXIF data (dimensions, GPS, orientation degrees, camera make/model, video duration, color space)
- `ProxyFile` — generated web-optimized assets (thumbnail, preview, 720p H.264, blurhash string); stored on local SSD
- `Burst` — groups rapid-fire photos with a `primary_asset_id` cover; prevents duplicate memories in flashbacks
- `AssetInteraction` — per-user state (starred, liked, hidden, view count, last viewed); unique on `(user_id, asset_id)`
- `UserStorageConfig` — NAS root paths per user; unique on `(storage_config_id, file_path)`
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

### Worker Media Processing

- Crawls NAS → extracts EXIF via ExifLib/ImageSharp → computes XXHash64 `content_hash` → writes `MediaAsset` + `Metadata`
- Generates proxy types: thumbnail, preview image, 720p H.264 video, blurhash string
- FFmpeg transcodes video with Intel QuickSync GPU acceleration via `/dev/dri` passthrough
- Burst detection: groups rapid-fire sequences, assigns `primary_asset_id` cover
- Reconciliation: periodic NAS scan; soft-deletes missing files (preserves user interactions); hash match re-links moved files

## Conventions

- **Namespaces**: File-scoped, matching `{AssemblyName}.{Layer}` (enforced as warning in `.editorconfig`)
- **Nullability**: Enabled globally — treat as baseline
- **Implicit usings**: Enabled globally — no need to import common `System.*` namespaces
- **Private fields**: `_camelCase` for instance, `PascalCase` for static
- **Interfaces**: Prefixed with `I`
- **Indentation**: 4 spaces, CRLF line endings
- **Analyzers**: Run at `Recommended` severity during build — fix warnings, don't suppress
- **NuGet versions**: Centralized in `Directory.Packages.props` — never specify a version in individual `.csproj` files

## Multi-User Design

Multiple users can share the same NAS paths (shared family library). Each user has private `AssetInteraction` state — never shared. `UserStorageConfig → MediaAsset` scopes assets to a user's assigned NAS root. Multiple worker instances can each monitor a separate config.

## Infrastructure

- Worker Dockerfile targets `mcr.microsoft.com/dotnet/runtime:10.0-noble` (not Alpine) specifically for FFmpeg + Intel QuickSync GPU support
- API Dockerfile uses Alpine (no GPU/FFmpeg needed)
- CI/CD runs on GitHub Actions (`.github/workflows/ci.yml`); API and Worker images built in parallel, tagged with `0.0.<run_number>` (and `latest` on master)
- PostgreSQL connection is never hardcoded — always read from `POSTGRES_CONNECTION__*` env vars or Docker secrets

## Development Roadmap

The project is in early stages. Epic 1 (Foundation & Infrastructure) is largely complete. Remaining epics in order:

1. **Epic 2**: User management & JWT authentication
2. **Epic 3**: Full media ingestion pipeline (EXIF, hashing, proxies, bursts)
3. **Epic 4**: NAS reconciliation (soft-delete, move detection, deduplication)
4. **Epic 5**: API endpoints (flashback queries, file serving, interactions)
5. **Epic 6**: Flashback UI (story player, Live Photo, blurhash, pre-fetching)
6. **Epic 7**: Recaps & calendar views
7. **Epic 8**: Email notifications with deep links

Additional context is in `../Anichron-Documentation/` (adjacent repo): `requirements.txt`, `solution-overview.txt`, `roadmap.md`, `database/database-decisions.txt`, and an ERD in `database/erd-mermaid.txt`.
