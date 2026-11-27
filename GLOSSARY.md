# Anichron

A self-hosted photo library that resurfaces a user's own photos and videos from their NAS as daily flashbacks.

## Language

**Memory**:
One thing shown in a flashback: a photo, a video, a Live Photo, or a burst, which is shown by its cover.
_Avoid_: Item, asset (in the UI), card

**On This Day**:
The user's memories whose capture date falls on a given day and month in an earlier year: today's, or one of the recent days the user picks. In a year without 29 February, memories from 29 February fall on 28 February.
_Avoid_: Today's memories, anniversary

**Recent day**:
A day before today whose On This Day has memories. Today's recent days run newest first and reach back at most a year.
_Avoid_: Earlier day, past day

**Story**:
The full-screen player that shows memories one after another, each for a set time, until the user moves on or pauses.
_Avoid_: Slideshow, carousel, reel

**Year group**:
The memories of one year within a day's story, shown together and skipped together.
_Avoid_: Chapter, section

**Burst**:
Photos of the same scene from one storage config, taken moments apart, shown as one memory. Either the camera marked them as one burst, or each one looks like a neighbouring frame taken shortly before or after it.
_Avoid_: Moment, similar group, near-duplicates

**Cover**:
The member of a burst that represents it wherever the burst is shown as one memory: the earliest member the viewing user has not hidden.
_Avoid_: Primary photo, key photo

**Live Photo**:
A photo and the short video the camera recorded with it, shown as one memory. Its video is part of the photo, never a memory of its own; without its video, the photo is shown as a still photo.
_Avoid_: Pair, HEIC+MOV, motion photo

**Poster**:
The still frame that stands for a video wherever it is shown before it plays. Only a video that is a memory of its own has one; a Live Photo's photo is already its image.
_Avoid_: Cover, thumbnail, preview frame

**Hidden**:
A memory, or one frame of a burst, that the user took out of their own stories. It stays in the library, listed in Hidden memories newest hidden first, and the user can bring it back from there.
_Avoid_: Archived, removed, deleted

**Capture date**:
The date and time on the clock where a photo or video was taken, as its camera or app recorded it, with the offset from UTC when one was recorded and the time zone where it was taken when its location is known. A moment recorded only in UTC is read in that zone, or in the library's time zone without a location. A capture date recorded without a time names the day only, and its asset is never part of a burst.
_Avoid_: Date taken, timestamp, creation date

**Capture zone**:
The time zone where a photo or video was taken, found from its GPS position. It never changes a recorded clock time or offset; when they disagree, both are kept.
_Avoid_: Timezone, location zone, offset

**Undated**:
An asset whose file records no capture date. It is kept and can be shown, but never appears in On This Day or a burst.
_Avoid_: No date, unknown date

**Storage config**:
A folder on the NAS that an admin assigned to one user, whose photos and videos become that user's memories. A user without one has no memories to show.
_Avoid_: Library, source, share, mount

**Missing**:
An asset whose file a complete crawl of its storage config did not find at its path.
_Avoid_: Gone, lost, deleted

**Soft-deleted**:
An asset hidden from every view because its file is missing, its path now holds other content, or it is a Live Photo's video whose photo now has another video; the asset and the user's stars, likes and hidden flag are kept so it can come back.
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

**Superseded**:
A Live Photo's video whose photo now has another video. It stays soft-deleted even while its file is still on the NAS, because it is never a memory of its own.
_Avoid_: Replaced, orphaned video, let-go video

**Forced password change**:
The state of an account whose user can only change their password or sign out until the change succeeds. It is set when an admin creates the account or resets its password, and for the first admin until they choose their own.
_Avoid_: Temporary password, must-change, password reset
