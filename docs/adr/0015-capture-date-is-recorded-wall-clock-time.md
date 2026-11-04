# A capture date is the recorded wall-clock time, or none

Status: accepted (2026-10-31, #383), amended 2026-11-01 (date-only dates) and 2026-11-03 (the zone
where it was taken), and 2026-11-04 (an unreadable offset). Decided by the maintainer on #383 and #404.

An asset's capture date is the time on the clock where the photo or video was taken, as its
file records it, stored as a local date and time without a zone. Next to it the asset keeps the
offset from UTC when the file records one, which kind of source the date came from, and the time
zone where it was taken when its location is known. A file that records no capture date is
**undated**: it has no capture date at all, and so never appears in On This Day or a burst. The
file's last-modified time is never used as a capture date.

- **Sources, first one wins as a whole** (time and offset from the same source):
  photos take EXIF `DateTimeOriginal` (+ `OffsetTimeOriginal`), then XMP
  `photoshop:DateCreated`, then IPTC `DateCreated` (+ `TimeCreated`); videos take Apple's
  QuickTime creation date (with offset), then AVI `DateTimeOriginal`, then the movie header's
  `Created`. A source that is absent or unparseable falls through; a movie header `Created` of
  zero (1904-01-01) counts as absent. A source is its time: a blank offset is no offset, and an
  offset that does not parse is dropped while the time it belongs to still dates the asset.
- **The time zone comes from the location.** An asset with a GPS position (EXIF GPS for photos,
  `com.apple.quicktime.location.ISO6709` for videos) gets the IANA zone at that position, looked
  up offline (GeoTimeZone; its boundary data is ODbL and is attributed in
  `THIRD-PARTY-NOTICES.md`). Without a position, the zone is unknown, never guessed.
- **The recorded value wins.** The zone never changes a recorded clock time or offset. When a
  recorded offset disagrees with the zone at that moment (a camera still on home time), both are
  kept as they are; a view after 1.0 lists such assets (#417).
- **A moment recorded only in UTC** (the movie header) is read in the zone where it was taken,
  or, without a position, in the library's time zone, the host's system zone that the API also
  uses for "today". It keeps no offset, since the file recorded none.
- **A date without a time** (XMP `photoshop:DateCreated` written as a date alone, IPTC
  `DateCreated` without `TimeCreated`) still wins when it comes first, and dates the asset by day
  alone. Its source kind says so, and such a photo is never a burst candidate.
- **A Live Photo's video** takes its photo's capture date, offset, source and zone.
- **Apple's creation date is read from its raw value** (#415). MetadataExtractor 2.9.3 converts it
  to the host's zone and drops its offset, and throws on a value it cannot parse. Until #415, a
  video that carries one stays undated rather than dated by its movie header, so nothing derived
  from GPS stands in for a recorded clock time.

## Why

On This Day groups by the day a photo was taken *where it was taken*: a photo from 30 October
in New Zealand is a 30 October memory wherever the library runs. A stored instant would make
that day depend on the zone it is read in; the wall-clock time plus an optional offset keeps the
day fixed and still shows the zone when one is known.

The last-modified time was the old fallback. On a NAS it is usually when the file was copied or
synced, so it put videos and screenshots on the wrong day and year. An undated asset is honest;
a wrong date is a wrong memory.

An offset is not a zone: `+13:00` is Auckland in summer and Tonga all year, and few files record
one (14 of 332 EXIF-dated photos in the survey below). The position names the zone, and most
phones record one. A moment recorded only in UTC lands on its local day only when read in the
zone where it was taken; the library's zone is right only at home.

A date without a time still names the right day, so it belongs in On This Day. It cannot place
a photo within the day: read as midnight, similar shots taken minutes or hours apart would fall
inside a burst's gap and be grouped as one.

## Considered options

- **Instant with offset:** the day would move with the reader's zone.
- **UTC for UTC-only sources:** recordings near midnight land on the wrong day far from UTC.
- **A per-user zone setting:** more than v1.0 needs; the position covers travel, and one
  deployment-wide zone covers the rest.
- **The offset alone** (the 2026-10-31 decision): no zone, and rarely recorded.
- **The zone derived when reading, from the stored coordinates:** the lookup would move into the
  API, and no query could find the assets whose offset disagrees with their zone.
- **An offset derived from the GPS timestamp:** a GPS fix can be minutes or hours old, and it
  gives an offset, not a zone.
- **The zone at the position wins over the recorded value:** it would correct a camera on home
  time, but the stored date would no longer be what the file says.
- **An iPhone video dated by its movie header until #415:** within seconds of the recorded time,
  but from GPS rather than from the file.
- **Last-modified as a marked fallback in the same column:** undated assets would carry a date
  that every query must remember to filter out. Keeping the column empty makes "undated"
  impossible to leak into On This Day.
- **More sources** (EXIF `DateTimeDigitized`, `xmp:CreateDate`, GPS date, PNG `tIME`, file
  names): a survey of 703 sample files (MetadataExtractor's and Immich's test collections) found
  `DateTimeDigitized` filling a gap only on film scans, where it is the scan date;
  `xmp:CreateDate` alone only on Photoshop documents, where it is the document's creation; no
  real file dated only by GPS; PNG `tIME` is a modified time. A source joins the chain when a
  real file needs it.
- **A date-only source yields to a later source with a time:** the date and time would no
  longer come from one source, and two sources can disagree on the day.
- **A date-only source counts as undated:** it records the right day, and On This Day needs
  nothing more.

## Consequences

- Most photos from older cameras record neither an offset nor a position, so their capture dates
  carry no zone.
- The library's time zone is the host's `TZ`; the API and the Worker must run with the same one.
- A stored zone reflects the boundary data at ingestion; a later boundary change reaches an asset
  only when it is ingested again.
- iPhone videos are undated until #415, and so missing from On This Day.
- Files with no recorded date (stripped exports, many PNGs, some cheap cameras) are undated
  until an "Undated" view shows them.
