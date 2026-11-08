# A Live Photo's video is a stored fact

Status: accepted (2026-11-08, #370). Decided by the maintainer on #370 (2026-11-08).

Whether a row is a Live Photo's video is the column `MediaAssets.IsLivePhotoVideo`, not a photo
pointing at the row. `PairedMovResolver.PairAsync` sets it on the video it pairs, and nothing
clears it. The classifier reads it to decide `LoneLivePhotoVideo`: a flagged video crawled alone
follows only its photo and is never refreshed, moved, restored or retired on its own.

When pairing points a photo at a different video, `PairAsync` soft-deletes the video the photo
lets go of, unless the item's MOV half already retires it. The let-go video keeps its flag, so it
stays retired for ever, whether its file is gone or still on disk.

The missing-file sweep and the read side still follow `PairedAssetId`: a Live Photo is retired
as one through its photo, and a video is hidden behind the visible photo that points at it.

## Why not derive it from the pointer

Before #370 a row was a Live Photo's video while some photo, active or soft-deleted, pointed at
it (an `EXISTS` in the classification query). A pointer can go: pairing hands a video over, a
photo lets go of its video, and a purge removes a soft-deleted photo. After `mv A.heic C.heic`
beside a stored `C.mov`, A points at `C.mov`, nothing points at A's old video, and `A.mov`
crawled alone was then a standalone video: `Unchanged` and shown as a memory with no poster and
A's date, or `AutoRestored` if pairing had retired it. Retiring it at pairing alone does not
hold, because the next lone crawl restores it.

- **Making the let-go video a standalone video** when its file is on disk matches a fresh ingest,
  but it shows a Live Photo's video as a memory of its own until a lone crawl gives it a poster,
  needs a classifier rule keyed on proxies, and wrongly retires it when an unreadable folder
  hides its file.
- **Keeping the photo on its old video** while that file is on disk would leave the item's own
  MOV half without a row, and the pipeline cannot ingest a MOV half alone.
- **A `MediaType` value** instead of a column would publish the role in the API's `mediaType` and
  touch every proxy and processing choice keyed on `MediaType.Video`.

## Consequences

- A real file on the NAS is never shown, which is not what a fresh ingest would produce.
- A former standalone video stays flagged once a photo has been paired with it, even after that
  photo lets go of it.
- A photo moved back beside its old video keeps its newer one: the old video is a
  `LoneLivePhotoVideo` for a MOV half that is not the photo's own.
- Dropping the column once there is data brings the orphaned videos back, and the rows it marked
  would have to be re-derived.
