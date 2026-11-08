# Data model

The domain entities and the decisions behind their mapping. Code: `Anichron.Core/Domain/`, `AnichronDbContext`.

- **`docs/schema.sql` is the authority** on columns, types, indexes and delete behaviour. It is generated (`scripts/derive-db-docs.sh`) and checked by CI. Never list columns here.
- **Why a column exists goes in `.HasComment(...)`**, next to its Fluent config, not in Markdown (ADR 0004).

## Entities

Fluent API only (`domain-has-no-data-annotations`). Columns are PascalCase. Table names can differ from entity names.

| Entity | What it is |
|---|---|
| `MediaAsset` | One file under a storage config: path, `ContentHash`, capture date (ADR 0015), media type, soft-delete flag, `PairedAssetId` (Live Photo → its MOV) |
| `Metadata` | 1:1 with an asset: the owned `MediaDescription` (size, GPS, orientation, camera, duration) and the BlurHash |
| `ProxyFile` | One generated file per asset and `ProxyType` |
| `Burst` | Photos of one config taken moments apart; `PrimaryAssetId` is the cover |
| `BurstCheck` | One row per photo burst detection has checked |
| `AssetInteraction` | Per-user star, like, hide, views |
| `UserStorageConfig` | A user's NAS root |
| `User`, `RefreshToken`, `Invite` | Identity; expired refresh tokens are deleted by the Worker's `TokenCleanup` |
| `WorkItem` | One unit of Worker work (`work-queue.md`) |
| `ProxyOrphan` | A file a proxy sweep found with no `ProxyFile` row |

## Decisions

- **Dedup is per storage config.** One *active* asset per `(StorageConfigId, ContentHash)`: a partial unique index, which is how a moved file finds its row again. The same bytes under two configs are two assets. A global unique `ContentHash` would break multi-user (`ModelInvariantsTests`).
- **One asset per path** within a config, soft-deleted rows included, so auto-restore finds a row at its path.
- **`RootPath` is canonical, globally unique, strictly inside `/data/originals`, and never nests** in another config's root (`StoragePath.Canonicalize`, `Overlaps`).
- **`Month`/`Day` are columns** with a composite index, so On This Day is an index lookup rather than an `EXTRACT()` scan.
- **The soft-delete filter is the only global query filter.** It covers `MediaAsset` and everything hanging off it. Per-user rules (hidden, ownership) are query-level, because the Worker shares the context and has no user (`ModelInvariantsTests`).
- **Username and email are normalised by a value converter**, on writes and comparisons alike. The InMemory provider skips it for query parameters, so test reads through `ToQueryString()` on Npgsql.
- **NodaTime for every date and time** (`domain-uses-nodatime`). Convert a `DateTime` from `System.IO` at that boundary.
- **A `MediaDescription` instance belongs to one row.** EF tracks an owned value by its owner, so sharing one between rows loses values. `Unknown` returns a new value each time, and `AssetContent.Of` copies.
- `Invite` carries the only concurrency token (`xmin`).
