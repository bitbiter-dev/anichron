# Media reads

How the API reads a user's assets. Code: `Anichron.Core/Data/Repository/AssetViewReader.cs`, `SoftDeletedAssetReader.cs`, `MediaAssetVisibility.cs`, `Anichron.API`.

## Asset lists

- **Every list of visible assets goes through `IAssetViewReader`**, in one SQL statement projected into a flat `AssetView`. No API type touches a media entity (`api-reads-media-through-views`).
- Ownership is `OwnedBy` (owner, active config). `VisibleTo` is `OwnedBy` plus active; `SoftDeletedFor` is `OwnedBy` plus soft-deleted. Both hold under `IgnoreQueryFilters()`.
- Each selection declares the set it reads: **shown** (`NotHiddenFor`) or **hidden** (`HiddenFor`). The reader applies `VisibleTo` and that set's rule for the caller, and leaves out:
  - a Live Photo's paired MOV, which reaches the client only as the photo's `PairedMovId`;
  - undated assets.
- **A new list view is a new `IAssetSelection`** (e.g. `OnThisDay`, `BurstMembers`, `HiddenAssets`): its set, its filter on entity columns (`Apply`), and optionally its order and page over the joined rows (`Page`). The reader never changes.
- Grouping and ordering happen in memory, except `HiddenAssets`, which orders by `HiddenAt` then id, newest first, and pages by keyset on that pair in SQL (`AssetCursor`, encoded by `AssetCursors`). `FlashbackYears` collapses each burst to its cover (`Bursts.Cover`) and orders years newest first, starred first, then by capture time.
- On This Day for 28 February also matches 29 February in years without one. 29 February matches only itself.
- The published `mediaType` is `LivePhoto` only when the paired video is visible and has a `WebVideo`; otherwise `Image` (`AssetDtoMapper`, for every view through `IAssetMedia`).

## Soft-deleted assets

- **`ISoftDeletedAssetReader`**, separate from `IAssetViewReader`, so no selection can widen `VisibleTo`. It reads `SoftDeletedFor` and leaves out any row another of the owner's rows points at through `PairedAssetId`, whatever that row's status: a Live Photo's video folds into its photo, a video whose photo is active is left out, and a superseded video is an item of its own.
- Hidden and undated assets are listed, and bursts are not collapsed.
- `GET /api/v1/assets/soft-deleted` pages it newest `LastSeenOnNas` first, then id, by keyset in SQL (`AssetCursor`). Each item carries the media fields, the path, `lastSeenOnNas` and `reason`, and no interaction state.

## Proxy URLs

- A row keeps its `Id` when its content changes, so every proxy URL carries the content hash: `proxies/{assetId}/{type}?v={hash}` (`ProxyKinds.UrlOf`).
- Only a request whose `v` is the current hash is cached `immutable`. A stale or missing `v` gets the current file with `no-cache`, never a `404`.
- `compactPreview` is null until the Worker has made one. The client shows it instead of `preview` only on a small screen that decodes JPEG XL, decided once per session (`api/display.ts`), and maps it into `preview` in `memoryOf` (`api/memories.ts`), so prefetch and every screen read one URL.
- The proxy route serves an asset the caller owns, soft-deleted included (`FindOwnedProxyAsync`); anyone else gets `404`.
- The proxy route also accepts the `media_token` cookie (ADR 0013). `ProxyEndpoints` owns that scheme, privately, so no other route can accept the cookie.

## Interaction rules

`AssetInteraction` is per user:

- **hidden:** `HiddenAt` is set; every hide stamps it anew and an unhide clears it. Left out of that user's shown set at query level, not a global filter, and listed by `GET /api/v1/assets/hidden`, each asset alone (no burst).
- **starred:** always included, and sorted first within its year.
- **liked:** `DisplayWeight` 2.0 instead of 1.0. It is computed by `FlashbackWeight.For`, never stored.
