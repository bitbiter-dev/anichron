# The path holder wins

Status: accepted (decided on #303)

## Decision

When a crawled file at path P has content that the same storage config already stores at another path Q, the file is reconciled through **P's row**, whenever a row holds P:

- **`cp Q P`** (Q still on disk): P's active row is soft-deleted, and the file counts as a duplicate of Q (`OverwrittenByCopy`).
- **`mv Q P`** (Q gone): P's row keeps its id and interactions and takes Q's content, reused rather than regenerated. Q's row is soft-deleted (`OverwrittenByMove`). A soft-deleted row at P is auto-restored the same way.

Wherever a row holds the path, the path decides which asset the file is. This matches content edited in place (#250) and auto-restore at a path (#301).

## Rejected

**Q's row follows the file:** P's row would have to give up its path, but soft-deleted rows keep their path so auto-restore can find them. The missing-file sweep would reach this rule's end state anyway, two crawls later.

## Consequences

- Hard to reverse: the stars, likes and hidden flag on Q's row stay on a soft-deleted row.
- P's row and Q's soft-deleted row share proxy files, so nothing may delete a proxy file another row still references.
- `mv Q P` is two writes in one transaction, ordered so that Q's row leaves the active content-hash index before P's row takes the hash.
- Swapping the contents of two paths soft-deletes both rows for one crawl.
- A Live Photo's MOV runs the same rule against its photo's paired row.
