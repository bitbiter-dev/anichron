# Anichron

A self-hosted photo library that resurfaces a user's own photos and videos from their NAS as daily flashbacks.

## Language

**Burst**:
Photos of the same scene from one storage config, taken moments apart, shown as one memory. Either the camera marked them as one burst, or each one looks like a neighbouring frame taken shortly before or after it.
_Avoid_: Moment, similar group, near-duplicates

**Cover**:
The member of a burst that represents it wherever the burst is shown as one memory: the earliest member the viewing user has not hidden.
_Avoid_: Primary photo, key photo

**Missing**:
An asset whose file a complete crawl of its storage config did not find at its path.
_Avoid_: Gone, lost, deleted

**Soft-deleted**:
An asset hidden from every view because its file is missing or its path now holds other content; the asset and the user's stars, likes and hidden flag are kept so it can come back.
_Avoid_: Trashed, archived, deleted

**Auto-restore**:
A soft-deleted asset becoming visible again, with its interactions intact, because a crawl found its file at its path or its content elsewhere in the same storage config.
_Avoid_: Restore (alone), undelete, recover

**Overwritten**:
A file whose path now holds content that the same storage config already stores at another path, either as a copy of that file or because that file was moved there.
_Avoid_: Replaced, clobbered
