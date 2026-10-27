# Media reads

How the API reads asset lists for a user, and how a user's interactions shape flashbacks.

## Asset lists

**Asset lists are read through `IAssetViewReader`** (`Anichron.Core/Data/Repository/AssetViewReader.cs`, #253). It always applies `VisibleTo` and `NotHiddenFor` to the caller and leaves out a Live Photo's paired MOV (any row that a row visible to the caller references through `PairedAssetId`, so hiding the Live Photo does not surface its video; the MOV reaches the client only as the Live Photo's `PairedMovId`, #320, and a soft-deleted MOV not at all), then the caller's `IAssetSelection` (e.g. `OnThisDay`, which filters only on `MediaAsset` columns and also brings every member of a burst with a visible member on the day, or `BurstMembers`, one burst's members), then left-joins the caller's own interaction row, all projected into a flat `AssetView` in one SQL statement. A new list view adds a selection class; it never changes the reader, and no API type touches a media entity (architecture rule `api-reads-media-through-views`). Grouping and ordering happen in memory on the result: `FlashbackYears` collapses each burst to its cover (`Bursts.Cover`: the caller's earliest visible member, starred or liked when any member is, `burst { burstId, isCover, count }`), kept only when the cover falls on the requested day (`OnThisDay.FallsOnTheDay`), then orders years newest first, starred first, then capture time. The published `mediaType` is `"LivePhoto"` only when the DTO carries the live video URL (the paired MOV is visible and has a `WebVideo` proxy), otherwise `"Image"`; the stored `MediaType` stays `LivePhoto`, so a returning video needs no write (`AssetDtoMapper`, #321).

## Flashback Interaction Rules

`AssetInteraction` state is per user and shapes that user's flashback queries (Epic 5+):
- `hidden = true` → excluded from the user's flashback queries. Applied **at query level** by `NotHiddenFor(userId)` (#48), not a global filter; other users still see the asset
- `starred = true` → always included; sorted first within its year group
- `liked = true` → `DisplayWeight` 2.0 (otherwise 1.0), used for candidate weighting when a date has many assets across years. `DisplayWeight` is **computed** by `FlashbackWeight.For` in Core, never stored — there is no such column (#46)
