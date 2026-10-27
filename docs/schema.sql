CREATE TABLE "Users" (
    "Id" uuid NOT NULL,
    "Username" text NOT NULL,
    "Email" text NOT NULL,
    "PasswordHash" text NOT NULL,
    "IsAdmin" boolean NOT NULL,
    "IsDisabled" boolean NOT NULL,
    "MustChangePassword" boolean NOT NULL,
    "FailedLoginAttempts" integer NOT NULL,
    "LockedUntil" timestamp with time zone,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
);
COMMENT ON TABLE "Users" IS 'Owns storage configs and interactions. All identity writes happen in the API; the Worker only reads.';
COMMENT ON COLUMN "Users"."Username" IS 'Always stored trimmed and lowercased: an EF value converter applies IdentityNormalizer.Normalize on every write and to every equality comparison against this column, so the case-sensitive unique index cannot hold two spellings of one name.';
COMMENT ON COLUMN "Users"."Email" IS 'Always stored trimmed and lowercased: an EF value converter applies IdentityNormalizer.Normalize on every write and to every equality comparison against this column, so the case-sensitive unique index cannot hold two spellings of one name.';
COMMENT ON COLUMN "Users"."PasswordHash" IS 'Argon2id. The parameters live with the hasher, not here, so rotating them does not require a migration.';
COMMENT ON COLUMN "Users"."MustChangePassword" IS 'Set by an admin reset. MustChangePasswordMiddleware then 403s every route except the handful needed to change it.';


CREATE TABLE "Invites" (
    "Id" uuid NOT NULL,
    "TokenHash" character varying(44) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "UsedAt" timestamp with time zone,
    "CreatedByUserId" uuid,
    "UsedByUserId" uuid,
    CONSTRAINT "PK_Invites" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Invites_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Invites_Users_UsedByUserId" FOREIGN KEY ("UsedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);
COMMENT ON TABLE "Invites" IS 'Admin-issued registration tokens. Registration requires one; the first user (bootstrap admin) is exempt. Redemption is a read-then-write race, which is why this is the only table carrying a concurrency token.';
COMMENT ON COLUMN "Invites"."TokenHash" IS 'Hash, never the token itself. 44 chars — base64 of a 32-byte value.';


CREATE TABLE "RefreshTokens" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "TokenHash" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    CONSTRAINT "PK_RefreshTokens" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_RefreshTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
COMMENT ON TABLE "RefreshTokens" IS 'Issued and revoked by the API; expired rows are swept by the Worker''s TokenCleanup work item. One of the few tables both services touch.';
COMMENT ON COLUMN "RefreshTokens"."TokenHash" IS 'Hash, never the token itself — a database disclosure must not hand out usable sessions.';


CREATE TABLE "StorageConfigs" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "RootPath" text NOT NULL,
    "IsActive" boolean NOT NULL,
    CONSTRAINT "PK_StorageConfigs" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_StorageConfigs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
COMMENT ON TABLE "StorageConfigs" IS 'A NAS root assigned to one user. This is the boundary that scopes assets to a user and lets several Workers each monitor a separate config.';
COMMENT ON COLUMN "StorageConfigs"."RootPath" IS 'GLOBALLY unique, not per user: a path cannot be assigned to two configs across all users. Two users indexing the same directory would duplicate every asset and let one user''s config removal cascade away the other''s rows.';


CREATE TABLE "WorkItems" (
    "Id" uuid NOT NULL,
    "Kind" text NOT NULL,
    "Payload" jsonb NOT NULL,
    "DedupKey" text,
    "StorageConfigId" uuid,
    "RequestedBy" uuid,
    "RequestedAt" timestamp with time zone NOT NULL,
    "Status" text NOT NULL,
    "Attempt" integer NOT NULL,
    "Stalls" integer NOT NULL,
    "ClaimedBy" text,
    "StartedAt" timestamp with time zone,
    "LeaseUntil" timestamp with time zone,
    "FinishedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "CancelRequestedAt" timestamp with time zone,
    "ProgressDone" bigint NOT NULL,
    "ProgressTotal" bigint,
    "ProgressNote" text,
    "Result" jsonb,
    "Error" text,
    CONSTRAINT "PK_WorkItems" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_WorkItems_StorageConfigs_StorageConfigId" FOREIGN KEY ("StorageConfigId") REFERENCES "StorageConfigs" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_WorkItems_Users_RequestedBy" FOREIGN KEY ("RequestedBy") REFERENCES "Users" ("Id") ON DELETE SET NULL
);
COMMENT ON TABLE "WorkItems" IS 'Durable work for the Worker, scheduled by Workers or requested by an admin. Only IWorkQueue inserts rows; the API''s only other write is an admin''s cancel, and every other state change belongs to the Worker''s executor. Delivery is at least once, so handlers start from scratch and converge.';
COMMENT ON COLUMN "WorkItems"."Kind" IS 'WorkKind, persisted as text. Picks the payload type and the handler through the registry, never through a switch.';
COMMENT ON COLUMN "WorkItems"."Payload" IS 'The kind''s IWorkPayload, serialised with System.Text.Json and NodaTime settings.';
COMMENT ON COLUMN "WorkItems"."DedupKey" IS 'Opt-in coalescing. While an item with this key is Queued, enqueueing the same key returns that item instead of adding a second; the partial unique index enforces it.';
COMMENT ON COLUMN "WorkItems"."StorageConfigId" IS 'The storage config the work belongs to, or null for unscoped work. A per-user Worker claims only its user''s configs and unscoped work; deleting the config deletes its items.';
COMMENT ON COLUMN "WorkItems"."RequestedBy" IS 'The admin who asked for the work; null when a Worker''s scheduler enqueued it.';
COMMENT ON COLUMN "WorkItems"."Status" IS 'Queued, Running, Succeeded, Failed or Cancelled, persisted as text.';
COMMENT ON COLUMN "WorkItems"."Attempt" IS 'Incremented by every claim. Every write a Worker makes while running an item is fenced by (Id, Attempt), so an attempt that lost its lease changes nothing.';
COMMENT ON COLUMN "WorkItems"."Stalls" IS 'Lease expiries, counted by lease recovery. A graceful shutdown hands the item back without counting one; the item fails as stalled once the limit is reached.';
COMMENT ON COLUMN "WorkItems"."ClaimedBy" IS 'The claiming Worker for display: its WORKER__USER or ''all'', then its host name.';
COMMENT ON COLUMN "WorkItems"."LeaseUntil" IS 'Extended by every heartbeat. A Running item past it belongs to a dead or paused Worker and goes back to the queue.';
COMMENT ON COLUMN "WorkItems"."UpdatedAt" IS 'Moves on every insert, claim and transition, and on a heartbeat only when the progress snapshot changed, so a reader can ask what changed since a point in time.';
COMMENT ON COLUMN "WorkItems"."CancelRequestedAt" IS 'When an admin asked to cancel the item. A Queued item is cancelled at once; a Running one keeps running until the Worker''s next heartbeat reads this and cancels its handler. A requested item is never queued again: release and lease recovery end it Cancelled. A cancelled periodic item counts as that run, so the next one follows a full interval later.';
COMMENT ON COLUMN "WorkItems"."ProgressNote" IS 'The latest progress snapshot (done, optional total, note), overwritten by each heartbeat; no history is kept.';
COMMENT ON COLUMN "WorkItems"."Result" IS 'The handler''s results summary, written when the item succeeds, or when it is cancelled after its handler returned one; a Cancelled item''s results are partial.';


CREATE TABLE "ProxyOrphans" (
    "Id" uuid NOT NULL,
    "SweepId" uuid NOT NULL,
    "ProxyPath" text NOT NULL,
    "StorageConfigId" uuid,
    "SizeBytes" bigint NOT NULL,
    "LastWriteTime" timestamp with time zone NOT NULL,
    "DeletionOutcome" text,
    "ProcessedAt" timestamp with time zone,
    CONSTRAINT "PK_ProxyOrphans" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ProxyOrphans_WorkItems_SweepId" FOREIGN KEY ("SweepId") REFERENCES "WorkItems" ("Id") ON DELETE CASCADE
);
COMMENT ON TABLE "ProxyOrphans" IS 'A file under the proxy root that a ProxySweep found unreferenced by any ProxyFiles row, soft-deleted assets'' included, and last written more than an hour before the scan. Written only by the Worker; the API reads it. Nothing is deleted until an admin asks, and a ProxyDeletion re-checks each file before it removes it.';
COMMENT ON COLUMN "ProxyOrphans"."SweepId" IS 'The ProxySweep work item that found the file; retention of the item purges its report.';
COMMENT ON COLUMN "ProxyOrphans"."ProxyPath" IS 'Path relative to the proxy root, compared as is with ProxyFiles.ProxyPath.';
COMMENT ON COLUMN "ProxyOrphans"."StorageConfigId" IS 'The storage config named by the file''s directory, read through IProxyDirectoryStrategy; null when the path names none. No foreign key: the config may be gone already, or go later.';
COMMENT ON COLUMN "ProxyOrphans"."SizeBytes" IS 'Size at the scan. A deletion skips the file when its size has changed since.';
COMMENT ON COLUMN "ProxyOrphans"."LastWriteTime" IS 'Last write time at the scan, truncated to microseconds. A deletion skips the file when it has changed since.';
COMMENT ON COLUMN "ProxyOrphans"."DeletionOutcome" IS 'Deleted, SkippedReferenced, SkippedChanged, Missing or Failed, persisted as text; null until a deletion processed the file. A processed orphan is never processed again.';


CREATE TABLE "BurstChecks" (
    "AssetId" uuid NOT NULL,
    "CheckedAt" timestamp with time zone NOT NULL,
    "IsCandidate" boolean NOT NULL,
    "CameraBurstId" text,
    CONSTRAINT "PK_BurstChecks" PRIMARY KEY ("AssetId")
);
COMMENT ON TABLE "BurstChecks" IS 'One row per photo burst detection has checked; no row means unchecked. Owned by the BurstDetection work item, which reads the original once when it checks a photo. A file edited in place loses its row, so it is checked again.';
COMMENT ON COLUMN "BurstChecks"."IsCandidate" IS 'The photo has an EXIF capture time, a camera make and model, and a thumbnail; only candidates are grouped. False also when the original could not be read.';
COMMENT ON COLUMN "BurstChecks"."CameraBurstId" IS 'Apple''s BurstUUID from the original''s maker note: photos of one storage config sharing it form one burst. Null when the camera wrote none.';


CREATE TABLE "Bursts" (
    "Id" uuid NOT NULL,
    "PrimaryAssetId" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Bursts" PRIMARY KEY ("Id")
);
COMMENT ON TABLE "Bursts" IS 'Groups photos of one storage config taken moments apart, so a flashback shows one memory rather than nine near-identical frames. Written only by the Worker''s BurstDetection work item, which also repairs bursts: one left with fewer than two active members is deleted, and so is one with no members at all.';
COMMENT ON COLUMN "Bursts"."PrimaryAssetId" IS 'The stored cover, the earliest member. SetNull on delete, so deleting the cover''s row (a storage config removal cascading to its assets) cannot fail on the burst; the next BurstDetection run picks a new cover or deletes the burst.';


CREATE TABLE "MediaAssets" (
    "Id" uuid NOT NULL,
    "StorageConfigId" uuid NOT NULL,
    "BurstId" uuid,
    "PairedAssetId" uuid,
    "FilePath" text NOT NULL,
    "FileName" text NOT NULL,
    "ContentHash" text NOT NULL,
    "DateCaptured" timestamp without time zone NOT NULL,
    "Month" integer NOT NULL,
    "Day" integer NOT NULL,
    "Year" integer NOT NULL,
    "MediaType" text NOT NULL,
    "IsSoftDeleted" boolean NOT NULL,
    "LastSeenOnNas" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MediaAssets" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MediaAssets_Bursts_BurstId" FOREIGN KEY ("BurstId") REFERENCES "Bursts" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_MediaAssets_MediaAssets_PairedAssetId" FOREIGN KEY ("PairedAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_MediaAssets_StorageConfigs_StorageConfigId" FOREIGN KEY ("StorageConfigId") REFERENCES "StorageConfigs" ("Id") ON DELETE CASCADE
);
COMMENT ON TABLE "MediaAssets" IS 'One row per media file, scoped to a storage config. Written ONLY by the Worker — the API never writes media data.';
COMMENT ON COLUMN "MediaAssets"."PairedAssetId" IS 'Live Photo pairing: the HEIC parent points at its MOV child. SetNull on delete so losing one half leaves the other a usable standalone asset.';
COMMENT ON COLUMN "MediaAssets"."FilePath" IS 'Path relative to the storage config root. Not to be confused with ProxyFiles.ProxyPath, which is relative to the proxy root.';
COMMENT ON COLUMN "MediaAssets"."ContentHash" IS 'XXHash64 of the file contents. Dedup is deliberately CONFIG-SCOPED: the same bytes under two storage configs are two assets, which is what makes multi-user work. A global unique index on this column alone would break that and must never be added.';
COMMENT ON COLUMN "MediaAssets"."Month" IS 'Stored apart from DateCaptured so "On This Day" is an index lookup on (Month, Day) rather than an EXTRACT() scan over every row.';
COMMENT ON COLUMN "MediaAssets"."Day" IS 'See Month — the pair backs IX_MediaAsset_Flashback.';
COMMENT ON COLUMN "MediaAssets"."IsSoftDeleted" IS 'Set by reconciliation when a complete crawl did not find the file, or when its path now holds content stored at another path. Soft rather than hard so the user''s interactions survive, and a crawl that finds the file again clears it (auto-restore).';


CREATE TABLE "Interactions" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "AssetId" uuid NOT NULL,
    "IsStarred" boolean NOT NULL,
    "IsLiked" boolean NOT NULL,
    "IsHidden" boolean NOT NULL,
    "LastViewed" timestamp with time zone,
    "ViewCount" integer NOT NULL,
    CONSTRAINT "PK_Interactions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Interactions_MediaAssets_AssetId" FOREIGN KEY ("AssetId") REFERENCES "MediaAssets" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Interactions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
COMMENT ON TABLE "Interactions" IS 'Per-user state for one asset. Never shared between users: two people looking at the same file each get their own row. Drives flashback weighting — hidden excludes, starred always includes and sorts first, liked raises DisplayWeight.';


CREATE TABLE "Metadata" (
    "AssetId" uuid NOT NULL,
    "Width" integer NOT NULL,
    "Height" integer NOT NULL,
    "Orientation" integer NOT NULL DEFAULT 1,
    "Latitude" double precision,
    "Longitude" double precision,
    "CameraMake" text,
    "CameraModel" text,
    "LensModel" text,
    "DurationInSeconds" integer,
    "Blurhash" text,
    CONSTRAINT "PK_Metadata" PRIMARY KEY ("AssetId"),
    CONSTRAINT "FK_Metadata_MediaAssets_AssetId" FOREIGN KEY ("AssetId") REFERENCES "MediaAssets" ("Id") ON DELETE CASCADE
);
COMMENT ON TABLE "Metadata" IS 'EXIF data, 1:1 with MediaAsset and keyed by it — there is no separate Id. Populated by ExifExtractionMiddleware; every field is best-effort, because a file may carry no EXIF at all.';
COMMENT ON COLUMN "Metadata"."Orientation" IS 'Raw EXIF orientation tag, 1-8 (1 = upright, also used when absent or invalid). Stored raw because 5 and 7 combine a rotation with a mirror and cannot be expressed as degrees; a reader converts, and for 5-8 swaps Width/Height. Proxies keep this tag on unrotated pixels, so do not rotate them twice.';
COMMENT ON COLUMN "Metadata"."Blurhash" IS 'BlurHash placeholder string (4x3 components), computed by the Worker from the upright image. Kept here rather than as a proxy file so a response needs no disk read for it. Null for videos, and for images ingested before the column existed, which are not backfilled.';


CREATE TABLE "ProxyFiles" (
    "Id" uuid NOT NULL,
    "AssetId" uuid NOT NULL,
    "ProxyPath" text NOT NULL,
    "ProxyType" text NOT NULL,
    "SizeBytes" bigint NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ProxyFiles" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ProxyFiles_MediaAssets_AssetId" FOREIGN KEY ("AssetId") REFERENCES "MediaAssets" ("Id") ON DELETE CASCADE
);
COMMENT ON TABLE "ProxyFiles" IS 'One row per generated artefact on the local SSD. An asset has several, but at most one per ProxyType, so of two attempts racing to add the same proxy only one row lands. Written only by ProxyStagingWriter, at the moment the file reaches its final name.';
COMMENT ON COLUMN "ProxyFiles"."ProxyPath" IS 'Path relative to the proxy root, sharded by CONTENT rather than by ingestion attempt so a retry overwrites its own debris instead of stranding it. IProxyDirectoryStrategy is the single place that rule lives.';
COMMENT ON COLUMN "ProxyFiles"."ProxyType" IS 'Thumbnail, WebVideo or FullPreview, persisted as text. The BlurHash placeholder is not a proxy: it is the Metadata.Blurhash column.';


CREATE INDEX "IX_BurstChecks_CameraBurstId" ON "BurstChecks" ("CameraBurstId");


CREATE INDEX "IX_Bursts_PrimaryAssetId" ON "Bursts" ("PrimaryAssetId");


CREATE INDEX "IX_Interactions_AssetId" ON "Interactions" ("AssetId");


CREATE UNIQUE INDEX "IX_Interactions_UserId_AssetId" ON "Interactions" ("UserId", "AssetId");


CREATE INDEX "IX_Invites_CreatedByUserId" ON "Invites" ("CreatedByUserId");


CREATE UNIQUE INDEX "IX_Invites_TokenHash" ON "Invites" ("TokenHash");


CREATE INDEX "IX_Invites_UsedByUserId" ON "Invites" ("UsedByUserId");


CREATE INDEX "IX_MediaAsset_Flashback" ON "MediaAssets" ("Month", "Day");


CREATE INDEX "IX_MediaAssets_Active" ON "MediaAssets" ("IsSoftDeleted") WHERE "IsSoftDeleted" = false;


CREATE INDEX "IX_MediaAssets_BurstId" ON "MediaAssets" ("BurstId");


CREATE INDEX "IX_MediaAssets_PairedAssetId" ON "MediaAssets" ("PairedAssetId");


CREATE UNIQUE INDEX ix_mediaassets_storageconfigid_contenthash_active ON "MediaAssets" ("StorageConfigId", "ContentHash") WHERE "IsSoftDeleted" = false;


CREATE INDEX ix_mediaassets_storageconfigid_contenthash_softdeleted ON "MediaAssets" ("StorageConfigId", "ContentHash") WHERE "IsSoftDeleted" = true;


CREATE INDEX "IX_MediaAssets_StorageConfigId_DateCaptured" ON "MediaAssets" ("StorageConfigId", "DateCaptured");


CREATE UNIQUE INDEX "IX_MediaAssets_StorageConfigId_FilePath" ON "MediaAssets" ("StorageConfigId", "FilePath");


CREATE UNIQUE INDEX ix_proxyfiles_assetid_proxytype ON "ProxyFiles" ("AssetId", "ProxyType");


CREATE INDEX ix_proxyfiles_proxypath ON "ProxyFiles" ("ProxyPath" text_pattern_ops);


CREATE UNIQUE INDEX ix_proxyorphans_sweepid_proxypath ON "ProxyOrphans" ("SweepId", "ProxyPath");


CREATE UNIQUE INDEX "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");


CREATE INDEX "IX_RefreshTokens_UserId_ExpiresAt" ON "RefreshTokens" ("UserId", "ExpiresAt");


CREATE UNIQUE INDEX "IX_StorageConfigs_RootPath" ON "StorageConfigs" ("RootPath");


CREATE INDEX "IX_StorageConfigs_UserId" ON "StorageConfigs" ("UserId");


CREATE UNIQUE INDEX ix_users_email ON "Users" ("Email");


CREATE UNIQUE INDEX ix_users_username ON "Users" ("Username");


CREATE INDEX ix_workitems_claim ON "WorkItems" ("RequestedAt") WHERE "Status" = 'Queued';


CREATE UNIQUE INDEX ix_workitems_dedupkey_queued ON "WorkItems" ("DedupKey") WHERE "Status" = 'Queued' AND "DedupKey" IS NOT NULL;


CREATE INDEX ix_workitems_lease ON "WorkItems" ("LeaseUntil") WHERE "Status" = 'Running';


CREATE INDEX "IX_WorkItems_RequestedBy" ON "WorkItems" ("RequestedBy");


CREATE INDEX "IX_WorkItems_StorageConfigId" ON "WorkItems" ("StorageConfigId");


CREATE INDEX "IX_WorkItems_UpdatedAt" ON "WorkItems" ("UpdatedAt");


ALTER TABLE "BurstChecks" ADD CONSTRAINT "FK_BurstChecks_MediaAssets_AssetId" FOREIGN KEY ("AssetId") REFERENCES "MediaAssets" ("Id") ON DELETE CASCADE;


ALTER TABLE "Bursts" ADD CONSTRAINT "FK_Bursts_MediaAssets_PrimaryAssetId" FOREIGN KEY ("PrimaryAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE SET NULL;
