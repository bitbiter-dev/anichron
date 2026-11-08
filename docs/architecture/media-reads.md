# Media reads

How the API reads a user's assets. Code: `Anichron.Core/Data/Repository/AssetViewReader.cs`, `Anichron.API`.

## Asset lists

- **Every list goes through `IAssetViewReader`**, in one SQL statement projected into a flat `AssetView`. No API type touches a media entity (`api-reads-media-through-views`).
- The reader always applies `VisibleTo` and `NotHiddenFor` for the caller, and leaves out:
  - a Live Photo's paired MOV, which reaches the client only as the photo's `PairedMovId`;
  - undated assets.
- **A new list view is a new `IAssetSelection`** (e.g. `OnThisDay`, `BurstMembers`). The reader never changes.
- Grouping and ordering happen in memory. `FlashbackYears` collapses each burst to its cover (`Bursts.Cover`) and orders years newest first, starred first, then by capture time.
- On This Day for 28 February also matches 29 February in years without one. 29 February matches only itself.
- The published `mediaType` is `LivePhoto` only when the paired video is visible and has a `WebVideo`; otherwise `Image` (`AssetDtoMapper`).

## Proxy URLs

- A row keeps its `Id` when its content changes, so every proxy URL carries the content hash: `proxies/{assetId}/{type}?v={hash}` (`ProxyKinds.UrlOf`).
- Only a request whose `v` is the current hash is cached `immutable`. A stale or missing `v` gets the current file with `no-cache`, never a `404`.
- The proxy route also accepts the `media_token` cookie (ADR 0013). `ProxyEndpoints` owns that scheme, privately, so no other route can accept the cookie.

## Interaction rules

`AssetInteraction` is per user:

- **hidden:** left out of that user's flashbacks by `NotHiddenFor`, at query level, not a global filter.
- **starred:** always included, and sorted first within its year.
- **liked:** `DisplayWeight` 2.0 instead of 1.0. It is computed by `FlashbackWeight.For`, never stored.
