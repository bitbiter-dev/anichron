# A capture date is the recorded wall-clock time, or none

Status: accepted (decided on #383 and #404)

## Decision

An asset's capture date is the clock time where it was taken, as its file records it, stored as a local date and time. Beside it the asset keeps:

- the UTC offset, when the file records one;
- the kind of source the date came from;
- the time zone where it was taken, when the location is known.

A file that records no date is **undated**. It never appears in On This Day or a burst. The file's last-modified time is never a capture date.

- **Sources, first one wins as a whole:**
  - Photos: EXIF `DateTimeOriginal` (+ `OffsetTimeOriginal`), then XMP `photoshop:DateCreated`, then IPTC `DateCreated` (+ `TimeCreated`).
  - Videos: Apple's QuickTime creation date, then AVI `DateTimeOriginal`, then the movie header's `Created`.
  - An absent or unparseable source falls through. An unparseable offset is dropped and the time kept.
- **The zone comes from the GPS position**, looked up offline (GeoTimeZone). Without a position the zone is unknown, never guessed.
- **The recorded value wins.** The zone never changes a recorded time or offset, even when the two disagree.
- **A moment recorded only in UTC** is read in the zone at its position, otherwise in the library's zone (the host's), and keeps no offset.
- **A date without a time** still wins when it comes first. It dates the asset by day only, and such a photo is never a burst candidate.
- **A Live Photo's video** takes its photo's whole capture date.

## Why

- **On This Day groups by the day where the photo was taken.** A stored instant would move that day with the reader's zone.
- **Last-modified time** on a NAS is usually when the file was copied, which puts memories on the wrong day. An undated asset is honest; a wrong date is a wrong memory.
- **An offset is not a zone.** Few files record one, and the GPS position names the zone.

## Rejected

| Option | Why not |
|---|---|
| Store an instant with offset | The day moves with the reader's zone |
| UTC for UTC-only sources | Recordings near midnight land on the wrong day |
| A per-user zone setting | More than v1.0 needs |
| Offset alone | Rarely recorded, and not a zone |
| Derive the zone when reading | Moves the lookup into the API; mismatches can't be queried |
| Offset from the GPS timestamp | The fix can be hours old |
| The zone corrects the recorded time | The stored date would no longer be what the file says |
| Last-modified as a marked fallback | Every query would have to filter it out |
| More sources (`DateTimeDigitized`, `xmp:CreateDate`, GPS date, PNG `tIME`, file names) | In a survey of 703 sample files, each filled a gap only with the wrong date (a scan date, a document's creation, a modified time) or never. A source joins when a real file needs it |
| A date-only source yields to a later timed one | Date and time from two sources can disagree on the day |
| A date-only source counts as undated | It names the right day, which is all On This Day needs |

## Consequences

- Photos from older cameras usually carry no zone.
- The API and every Worker must run with the same `TZ`.
- A stored zone reflects the boundary data at ingestion.
- Undated files stay hidden until an "Undated" view exists.
