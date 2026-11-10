# Ingestion

How the Worker turns files on the NAS into assets. Code: `Anichron.Worker/Crawling`, `Ingestion`, `Bursts`.

## Crawl

- One `Crawl` work item per storage config, due `Worker:CrawlIntervalHours` after the last one finished (`work-queue.md`).
- Walks folder by folder and never enters `CrawlExclusions` (dot-entries, NAS and OS system folders, app library packages) or the folder names in `Worker:ExcludedDirectories`.
- **Never follows a link.** Every link under the root is skipped at listing and counted as `skippedLinks`; the root itself is not checked.
- An unreadable subfolder is logged and skipped, which makes the walk incomplete. An unreadable or missing root fails the item.
- `MediaTypeDetector` decides what is ingested. A video format counts only when MetadataExtractor reads its size and duration (QuickTime, AVI; not `.mkv` or `.wmv`).
- A file it finds no type for (an `.AAE` sidecar, a `.txt`) is logged at `Debug` and counted as `skippedUnsupported`.
- Files flow through a bounded `Channel<T>` to `Worker:MaxConcurrentFiles` consumers. Processing is idempotent.

## One item, step by step

An item is one file, or both halves of a Live Photo.

1. **Hash** the content (XXHash64).
2. **Classify.** `IngestionClassifier` loads every relevant row in one query (`FindByHashesOrPathsAsync`) and gives each half a `FileOutcome`, the `Stored` row it acts on, the `Retired` row it soft-deletes, and a `PairingAction`. Nothing else asks the database whether content is known.
3. **Process new content** (`New`, `ContentChanged`, `AutoRestoredContentChanged`): metadata, proxies, then the `IOutcomeWriter` for that outcome. Every other outcome stops after `LivePhotoPairer` and `KnownFileReconciler`.
4. **Save once.** Steps only stage changes; `IngestionSaver.TrySaveItemAsync` commits the item in one save, so an item is atomic and a failed one is redone whole on the next crawl. A lost uniqueness race (`LostIngestionRace`) skips the item until the next crawl.

One home per rule: a row takes new content only through `StoredContent.TakeAsync`, is auto-restored only through `ReactivateAsync`, and is retired only through `RetireAsync`.

**Adding an outcome:** a classifier branch, a `FileOutcome` value and its handler or writer. Its writes go into the item's one save.

## Reconciliation

For a file with content H at path P, first match wins (ADR 0011 for the path-holder rule):

| Situation | Outcome |
|---|---|
| A video alone that matches a row with `IsLivePhotoVideo` set | `LoneLivePhotoVideo`: the video row is left alone (it follows its photo) |
| An active row holds H at P | `Unchanged` |
| An active row holds H at Q, Q still exists, an active row at P | `OverwrittenByCopy`: P's row is soft-deleted |
| … Q still exists, no active row at P | `DuplicateCopy`, reported |
| … Q gone, a row at P | `OverwrittenByMove`: P's row takes Q's content, Q's row is soft-deleted, in one transaction |
| … Q gone, no row at P | `Moved` |
| No active row holds H, an active row at P | `ContentChanged`: edited in place |
| A soft-deleted row at P | `AutoRestored`, or `AutoRestoredContentChanged` |
| A soft-deleted row holds H | `AutoRestoredMoved` (the most recently seen one) |
| Otherwise | `New` |

- A row that takes content keeps its `Id`, interactions and `PairedAssetId`; its burst check is cleared.
- An edit keeps the stored capture date unless the new content records its own.
- Auto-restore leaves interactions alone, so a hidden asset stays hidden.
- After the walk, `MissingFileSweep` soft-deletes the active rows whose path was not found, plus the video of every missing Live Photo. It does nothing when the walk was incomplete (`IncompleteWalk`) or found no media while rows exist (`EmptyRoot`).
- Swapping the contents of two paths can soft-delete both rows for one crawl; the next crawl restores them.

## Live Photos

- **One unit.** The photo is classified first. The MOV half then runs the same table against the photo's paired row, unless another row holds the MOV's path: the path holder wins and the photo is re-pointed to it.
- **A video has one photo, and a soft-deleted photo never points at an active video.** Pairing goes only through `PairedMovResolver.PairAsync`; a unique index on `PairedAssetId` backs it. Retiring a photo retires its video.
- **A video a photo lets go of is retired.** When `PairAsync` points a photo at another video, it soft-deletes the previous one through `StoredContent.RetireAsync`, unless the item's MOV half already retires it. Pairing sets `IsLivePhotoVideo`, which is never cleared, so the let-go video never comes back as a memory of its own (ADR 0019).
- The paired MOV is its own asset and owns its `WebVideo`, which `PairedMovProxyWriter` writes when the MOV takes content or still lacks one.
- The video takes its photo's whole capture date, in one place: `PairedMovResolver.DateFromPhoto`.

## Metadata

- `MetadataExtractionMiddleware.Extract` is pure. One reader per part in `Ingestion/Extraction`: `RecordedDates`, `Positions`, `Frames`, `Durations`.
- Capture date and time zone: ADR 0015. An undated file gets its proxies, but never appears in On This Day or a burst.
- The time zone is looked up from the GPS position (`CaptureZones`). `TZ` must be the same on the API and every Worker.
- Until MetadataExtractor ships upstream #475, `AppleCreationDates` reads a video's raw Apple creation date itself. The canary test `MetadataExtractor_StillHandsOverAppleCreationDateWithoutItsOffset` fails on the bump that fixes it.
- Orientation comes from where the pixels are actually turned: HEIC `irot`, a video's track rotation, otherwise EXIF. `Metadata.Orientation` applies only when serving an original image; a proxy is never rotated by it.

## Proxies

- `Thumbnail`, `FullPreview` and `WebVideo` are files; the BlurHash is a string in `Metadata.Blurhash`.
- A photo's come from the photo. A standalone video gets its `WebVideo` and a poster (thumbnail, preview, BlurHash) from its frame at 1 s. A Live Photo's video gets only its `WebVideo`.
- A photo is decoded once through `IUprightImageDecoder` (HEIC by its bytes to Magick.NET, ADR 0016; everything else to ImageSharp), then shrunk largest-first; each generator gets its own copy.
- Sizes are edge-based and never upscale. The video encoder is picked at runtime: QuickSync, NVENC, AMF, then software.
- A new `Worker:VideoShortEdge` sizes only videos ingested afterwards; an existing `WebVideo` keeps its size until its file changes. Its file name, `video.mp4`, names no size.
- Proxy paths derive from content (CLAUDE.md, "Storage Layout").

## Failure ownership

- `ProxyStagingWriter` is the only place a proxy reaches its final name: it writes a per-attempt `.tmp`, renames it, then registers the proxy on the item.
- When a step fails, `IngestionPipelineRunner` deletes only the proxies this attempt created and its temporary files. It removes a directory only when it is empty, never recursively, because other rows and attempts share content-derived paths.
- On a lost race, the item releases the MOV proxies it staged, because the winner owns them.
- A clean shutdown drops the in-flight temporary file, keeps completed proxies and re-throws the cancellation.
- Not reclaimable in-process: a hard kill, a storage config's cascade delete, and the old directory of a file edited in place. A proxy sweep reclaims them (`work-queue.md`, "Proxy sweeps").

## Bursts

ADR 0009 for the matcher.

- A `BurstDetection` work item per config, enqueued when its crawl finishes and scheduled as a fallback.
- It first repairs: a burst with fewer than 2 active members is deleted; otherwise its earliest active member becomes the cover.
- Then it checks each unchecked photo whose capture date has a time of day (`CaptureSources.Timed`), oldest first, and records a `BurstCheck`. A content change clears the check.
- A photo joins a burst when it has the same Apple `BurstUUID` as another, at any time, or when a photo of the same camera within `Worker:BurstGapSeconds` scores at least `Worker:BurstSimilarityThreshold`, unless the burst would then span more than `Worker:BurstSpanSeconds`. Bursts merge into the oldest.

## Duplicates

Reported, never merged. `SharedContentReporter` logs the copies within a config and across the same user's configs at the end of each crawl. Duplicates across users are expected and never reported.
