# The path holder wins

Status: accepted (2026-10-25, #303). Decided by the maintainer on #303.

A crawled file at path P whose content the same storage config already stores at another path Q
is reconciled through **P's row**, not Q's, whenever a row holds P:

- **`cp Q P`** (Q still on disk): P's active row is soft-deleted, because its content is no longer
  at its path, and the file at P counts as a duplicate copy of Q (`OverwrittenByCopy`).
- **`mv Q P`** (Q gone): P's row keeps its id and interactions and takes Q's content: hash,
  capture date, media type, `Metadata` and proxy paths, reused rather than regenerated. Q's row
  is soft-deleted (`OverwrittenByMove`). A soft-deleted row at P is auto-restored the same way.

This follows #250 (content edited in place updates the row at the path) and #301 (a soft-deleted
row at the path is auto-restored with the new content): wherever a row holds the path, the path
decides which asset the file is.

## Why not let the content's row follow the file

In a hash-keyed model, a reader would expect Q's row to move to P with `mv Q P`, as it does when
no row holds P (`Moved`, #35). That was weighed and rejected:

- P's row would have to be soft-deleted, but a soft-deleted row keeps its path in
  `IX_MediaAssets_StorageConfigId_FilePath` (soft-deleted rows included, so auto-restore can find
  it). Q's row could then not take P without P's row giving up its path, which reverses the
  premise of #33 that a soft-deleted row is found again at its path.
- #250 and #301 already let the path win. Without this rule, the missing-file sweep (#34) would
  reach the same end state anyway, two crawls later.

## Consequences

- Hard to reverse: stars, likes and the hidden flag a user set on Q's row stay on a soft-deleted
  row. Changing the rule later would not bring them back to P's row.
- P's row and Q's soft-deleted row reference the same proxy files, because a proxy's path is
  derived from the config and the content hash. Purging soft-deleted rows (#47) and the orphan
  sweeper (#174) must not delete files another row still references. For the same reason, a
  failed ingestion attempt never deletes a proxy file that existed before it.
- The two writes in `mv Q P` are two steps in one transaction: Q's row leaves the active
  content-hash index before P's row takes the hash, which EF Core cannot order on its own because
  the index is filtered.
- Swapping the contents of two paths can soft-delete both rows for one crawl; the next crawl
  auto-restores each with its path's new content.
- Live Photo MOV halves keep `DuplicateCopy` and `PathTaken`: a Live Photo moved onto another
  keeps the old video until treating a Live Photo as one unit lands (the follow-up to #303).
