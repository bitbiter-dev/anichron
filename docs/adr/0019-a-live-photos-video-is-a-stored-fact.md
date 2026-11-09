# A Live Photo's video is a stored fact

Status: accepted (decided on #370)

## Decision

Whether a row is a Live Photo's video is the column `MediaAssets.IsLivePhotoVideo`, not a photo pointing at the row. `PairedMovResolver.PairAsync` sets it on the video it pairs, and nothing clears it. The classifier reads it to decide `LoneLivePhotoVideo`: a flagged video crawled alone follows only its photo.

When pairing points a photo at another video, `PairAsync` soft-deletes the video the photo lets go of, unless the item's MOV half already retires it. The let-go video keeps its flag, so it stays retired, whether its file is gone or still on disk.

The missing-file sweep and the read side still follow `PairedAssetId`: a Live Photo is retired through its photo, and a video is hidden behind the photo that points at it.

## Why

- **A pointer can go:** pairing hands a video over, a photo lets go of its video, and a purge removes a soft-deleted photo. A role derived from the pointer then turns a Live Photo's video into a standalone memory with no poster and its photo's date.
- **Retiring at pairing alone does not hold:** the next lone crawl of the video would restore it.

## Rejected

- **Making the let-go video a standalone video** when its file is on disk: it shows a Live Photo's video as a memory until a lone crawl gives it a poster, needs a classifier rule keyed on proxies, and wrongly retires it when an unreadable folder hides its file.
- **Keeping the photo on its old video:** the item's own MOV half would be left without a row, and the pipeline cannot ingest a MOV half alone.
- **A `MediaType` value:** it would publish the role in the API's `mediaType` and touch every proxy and processing choice keyed on `MediaType.Video`.

## Consequences

- A real file on the NAS is never shown, which is not what a fresh ingest would produce.
- A former standalone video stays flagged once a photo has been paired with it, even after that photo lets go of it.
- A photo moved back beside its old video keeps its newer one: the old video is a `LoneLivePhotoVideo`.
- Dropping the column once there is data brings the let-go videos back, and the rows it marked would have to be re-derived.
