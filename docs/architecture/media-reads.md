# Media reads

How the API reads a user's assets. Code: `Anichron.Core/Data/Repository/AssetViewReader.cs`, `FlashbackReader.cs`, `SoftDeletedAssetReader.cs`, `MediaAssetVisibility.cs`, `Anichron.API`.

## Asset lists

- **Every list of visible assets goes through `IAssetViewReader`**, in one SQL statement projected into an `AssetView`. No API type touches a media entity (`api-reads-media-through-views`).
- Every view carries its media as an `AssetMedia`, projected only by `AssetMedia.Into`. A new media field or proxy type goes there.
- Ownership is `OwnedBy` (owner, active config). `VisibleTo` is `OwnedBy` plus active; `SoftDeletedFor` is `OwnedBy` plus soft-deleted. Both hold under `IgnoreQueryFilters()`.
- Each selection declares the set it reads: **shown** (`NotHiddenFor`) or **hidden** (`HiddenFor`). The reader applies `VisibleTo` and that set's rule for the caller, and leaves out:
  - a Live Photo's paired MOV, which reaches the client only as the photo's `PairedMovId`;
  - undated assets.
- **A new list view is a new `IAssetSelection`** (e.g. `AssetsById`, `BurstMembers`, `HiddenAssets`): its set, its filter on entity columns (`Apply`), and optionally its order and page over the joined rows (`Page`). The reader never changes.
- Grouping and ordering happen in memory, except `HiddenAssets`, which orders by `HiddenAt` then id, newest first, and pages by keyset on that pair in SQL (`AssetCursor`, encoded by `AssetCursors`). `ReadHiddenPageAsync` reads that page.
- **A keyset page is read by `AssetPage.ReadAsync`** in Core: one row more than the page, and the next cursor from its last row. Endpoints only format it.
- The published `mediaType` is `LivePhoto` only when the paired video is visible and has a `WebVideo`; otherwise `Image` (`AssetDtoMapper`, for every view through its `AssetMedia`).

## Flashbacks

- **`IFlashbackReader`**, separate from `IAssetViewReader` but on the same base (`EfAssetViewReader.Listed`), reads narrow keys first (`FlashbackItem`), then full views only for the memories it returns (`AssetsById`).
- A burst is one key: its cover, chosen in SQL over all its shown members, found by burst id, with the burst's starred and liked state and count.
- On This Day's rule is one predicate, `OnThisDay.Includes`: the same month and day in an earlier year. For 28 February it also matches 29 February in years without one; 29 February matches only itself.
- A day's memories run years newest first, then starred first, by capture time and id. A recent day shows the first of them.
- `GET /api/v1/flashbacks/days` (`ReadRecentDaysAsync`): SQL narrows the keys to the window's day ranges (`DayWindow.Covering`). `WalkAsync` reads the days just before `before` first, the rest of the year only when those are short, and takes each date's first key by `Includes` and the day's order.

## Soft-deleted assets

- **`ISoftDeletedAssetReader`**, separate from `IAssetViewReader`, so no selection can widen `VisibleTo`. It reads `SoftDeletedFor` and leaves out any row another of the owner's rows points at through `PairedAssetId`, whatever that row's status: a Live Photo's video folds into its photo, a video whose photo is active is left out, and a superseded video is an item of its own.
- Hidden and undated assets are listed, and bursts are not collapsed.
- `GET /api/v1/assets/soft-deleted` pages it newest `LastSeenOnNas` first, then id, by keyset in SQL (`AssetCursor`). Each item carries the media fields, the path, `lastSeenOnNas` and `reason`, and no interaction state. Every page also carries `total`, the count of the whole list.
- `POST /api/v1/assets/purges` accepts the requested ids the list holds, plus each accepted Live Photo's soft-deleted video (`ResolvePurgeAsync`). Any other id is left out without a trace. It enqueues one `AssetPurge` per storage config, with no dedup key, and nothing when nothing is accepted (`work-queue.md`, "Purges").

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
