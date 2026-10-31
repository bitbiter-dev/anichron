# A capture date is the recorded wall-clock time, or none

Status: accepted (2026-10-31, #383). Decided by the maintainer on #383.

An asset's capture date is the time on the clock where the photo or video was taken, as its
file records it, stored as a local date and time without a zone. Next to it the asset keeps the
offset from UTC when the file records one, and which kind of source the date came from. A file
that records no capture date is **undated**: it has no capture date at all, and so never appears
in On This Day or a burst. The file's last-modified time is never used as a capture date.

- **Sources, first one wins as a whole** (time and offset from the same source):
  photos take EXIF `DateTimeOriginal` (+ `OffsetTimeOriginal`), then XMP
  `photoshop:DateCreated`, then IPTC `DateCreated` (+ `TimeCreated`); videos take Apple's
  QuickTime creation date (with offset), then AVI `DateTimeOriginal`, then the movie header's
  `Created`. A source that is absent or unparseable falls through; a movie header `Created` of
  zero (1904-01-01) counts as absent.
- **A moment recorded only in UTC** (the movie header) is read in the library's time zone, the
  host's system zone that the API also uses for "today", and keeps no offset, so a guessed zone
  is never shown as a recorded one.
- **A Live Photo's video** takes its photo's capture date, offset and source.

## Why

On This Day groups by the day a photo was taken *where it was taken*: a photo from 30 October
in New Zealand is a 30 October memory wherever the library runs. A stored instant would make
that day depend on the zone it is read in; the wall-clock time plus an optional offset keeps the
day fixed and still shows the zone when one is known.

The last-modified time was the old fallback. On a NAS it is usually when the file was copied or
synced, so it put videos and screenshots on the wrong day and year. An undated asset is honest;
a wrong date is a wrong memory.

## Considered options

- **Instant with offset:** the day would move with the reader's zone.
- **UTC for UTC-only sources:** recordings near midnight land on the wrong day far from UTC.
- **A per-user zone setting:** more than v1.0 needs; one deployment-wide zone covers it.
- **Last-modified as a marked fallback in the same column:** undated assets would carry a date
  that every query must remember to filter out. Keeping the column empty makes "undated"
  impossible to leak into On This Day.
- **More sources** (EXIF `DateTimeDigitized`, `xmp:CreateDate`, GPS date, PNG `tIME`, file
  names): a survey of 703 sample files (MetadataExtractor's and Immich's test collections) found
  `DateTimeDigitized` filling a gap only on film scans, where it is the scan date;
  `xmp:CreateDate` alone only on Photoshop documents, where it is the document's creation; no
  real file dated only by GPS; PNG `tIME` is a modified time. A source joins the chain when a
  real file needs it.

## Consequences

- Most photos from older cameras record no offset, so most capture dates show without a zone.
- The library's time zone is the host's `TZ`; the API and the Worker must run with the same one.
- Files with no recorded date (stripped exports, many PNGs, some cheap cameras) are undated
  until an "Undated" view shows them.
