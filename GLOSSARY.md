# Anichron

A self-hosted photo library that resurfaces a user's own photos and videos from their NAS as daily flashbacks.

## Language

**Burst**:
Photos of the same scene from one storage config, taken moments apart, shown as one memory. Either the camera marked them as one burst, or each one looks like a neighbouring frame taken shortly before or after it.
_Avoid_: Moment, similar group, near-duplicates

**Cover**:
The member of a burst that represents it wherever the burst is shown as one memory: the earliest member the viewing user has not hidden.
_Avoid_: Primary photo, key photo

**Live Photo**:
A photo and the short video the camera recorded with it, shown as one memory. Its video is part of the photo, never a memory of its own; without its video, the photo is shown as a still photo.
_Avoid_: Pair, HEIC+MOV, motion photo

**Capture date**:
The date and time on the clock where a photo or video was taken, as its camera or app recorded it, with the offset from UTC when one was recorded. A moment recorded only in UTC is read in the library's time zone.
_Avoid_: Date taken, timestamp, creation date

**Undated**:
An asset whose file records no capture date. It is kept and can be shown, but never appears in On This Day or a burst.
_Avoid_: No date, unknown date

**Missing**:
An asset whose file a complete crawl of its storage config did not find at its path.
_Avoid_: Gone, lost, deleted

**Soft-deleted**:
An asset hidden from every view because its file is missing or its path now holds other content; the asset and the user's stars, likes and hidden flag are kept so it can come back.
_Avoid_: Trashed, archived, deleted

**Auto-restore**:
A soft-deleted asset becoming visible again, with its interactions intact, because a crawl found its file at its path or its content elsewhere in the same storage config.
_Avoid_: Restore (alone), undelete, recover

**Purge**:
The owner permanently forgetting a soft-deleted asset, together with its stars, likes and hidden flag. The file on the NAS is never touched; if it turns up again, it comes back as a new asset.
_Avoid_: Hard delete, permanently delete, empty trash

**Overwritten**:
A file whose path now holds content that the same storage config already stores at another path, either as a copy of that file or because that file was moved there.
_Avoid_: Replaced, clobbered
