# Schema documentation is generated, and its rationale lives in the schema

Status: accepted — implemented

We document the database in three layers, none of them hand-written prose about columns:

1. **Facts** — `docs/schema.sql`, produced by `dotnet ef dbcontext script` and committed. CI
   regenerates it and fails if anything moved.
2. **Rationale** — PostgreSQL `COMMENT`s, configured with `.HasComment(...)` in
   `AnichronDbContext` beside the Fluent config they explain.
3. **Navigable view** — an ER diagram and per-table pages, generated in CI by `tbls` against a
   throwaway database and published as a build artefact, never committed.

## What went wrong, which is the only reason to take this decision

The hand-written description of the schema had drifted badly, and nothing could tell:

- `Invite` was missing from the ER diagram entirely — an entire table, in both the wiki's copy and
  a separate documentation repository's copy
- Columns were named in `snake_case` throughout; the model has only ever emitted PascalCase. The
  one explicitly-named column in the whole schema is `Invites.xmin`
- `live_photo_pair_id` does not exist; the column is `PairedAssetId`
- `Metadata` was documented as carrying a *color space* field. There is no such column
- `ProxyFile` was documented as having a *blurhash string*. There is no such column — `BlurHash`
  is a `ProxyType` value whose content is a file
- The unique index on `(StorageConfigId, FilePath)` appeared in no document at all

None of these facts were ever undocumented. They were **duplicated** — restated by hand in prose
while the authoritative copy sat in `AnichronDbContextModelSnapshot.cs`, which is committed and
diffed in every pull request. The copy rotted because nothing compared it to anything.

## Why not simply generate a nicer document

That was the first attempt, and it was wrong in an instructive way: ~200 lines of C# rendering
Markdown from the EF model, with a test asserting the committed file matched. It worked. It was
also a **third** copy of the same facts, and a bespoke renderer to maintain forever, in a problem
space where mature tools already exist.

The rule this decision encodes: *do not hand-build a layer that a maintained tool already covers,
and do not restate a fact that version control already holds.*

## Why the rationale goes in `COMMENT`, not in Markdown

This is the part that actually prevents recurrence. Facts can be generated; *why* cannot — no tool
infers that `(StorageConfigId, ContentHash)` is deliberately non-unique, or that `Month` and `Day`
are split so "On This Day" is an index lookup rather than an `EXTRACT()` scan.

Keeping that prose in a separate file is what failed before. Keeping it in `.HasComment(...)` puts
it beside the Fluent configuration it explains, carries it into the database, and therefore into
`docs/schema.sql` and into any documentation tool pointed at a live instance. It is reviewed in the
same diff as the schema change that prompted it.

⚠️ The cost is real and worth stating: a comment change produces an EF migration. The first one
(`AddSchemaComments`) is 48 comment alterations and **zero** structural statements, so it carries
no data risk — but a one-word fix to a comment is still a migration, not a text edit.

📌 Index rationale stays in code comments. EF exposes no `HasComment` for an index, so the reasoning
for `IX_MediaAssets_Active`'s filter and the non-uniqueness of the content-hash index lives in
`AnichronDbContext` as ordinary comments and does not reach the database.

## Consequences

`docs/schema.sql` is generated and must never be edited by hand. `scripts/derive-db-docs.sh`
regenerates it; `--check` regenerates and fails on any change, which is the CI gate. The check
regenerates **in place and asks git** rather than diffing against a temporary file — process
substitution hands `diff` a `/dev/fd` path that some sandboxes and container runtimes refuse, and a
fixed temp path collides between concurrent runs. Both fail in ways that read as a broken script
rather than as drift.

Nothing in this decision needs a database, so the gate runs on any runner, including one with no
Docker daemon. The ER-diagram job does need one and is therefore separate, produces an artefact
rather than a committed file, and cannot block a merge.

We give up a browsable schema document in the repository. That is deliberate: a committed
human-readable copy is the thing that drifted, and the generated SQL — with its `COMMENT`s — is
both authoritative and readable enough for review.
