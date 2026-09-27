The Entity Framework tools version '10.0.7' is older than that of the runtime '10.0.12'. Update the tools for the latest features and bug fixes. See https://aka.ms/AAc1fbw for more information.
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
COMMENT ON TABLE "RefreshTokens" IS 'Issued and revoked by the API; expired rows are swept by the Worker''s TokenCleanupService. One of the few tables both services touch.';
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


CREATE TABLE "Bursts" (
    "Id" uuid NOT NULL,
    "PrimaryAssetId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Bursts" PRIMARY KEY ("Id")
);
COMMENT ON TABLE "Bursts" IS 'Groups a rapid-fire sequence so a flashback shows one memory rather than nine near-identical frames. The table and schema exist; burst DETECTION is not yet implemented in the Worker, so in a running system this table is empty.';
COMMENT ON COLUMN "Bursts"."PrimaryAssetId" IS 'The frame chosen to represent the burst. Restrict on delete: losing the cover would leave a group that cannot be rendered.';


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
COMMENT ON COLUMN "MediaAssets"."IsSoftDeleted" IS 'Set by reconciliation when a file is gone from the NAS. Soft rather than hard so the user''s interactions survive a temporarily unmounted share.';


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
    "OrientationDegrees" integer NOT NULL,
    "Latitude" double precision,
    "Longitude" double precision,
    "CameraMake" text,
    "CameraModel" text,
    "LensModel" text,
    "DurationInSeconds" integer,
    CONSTRAINT "PK_Metadata" PRIMARY KEY ("AssetId"),
    CONSTRAINT "FK_Metadata_MediaAssets_AssetId" FOREIGN KEY ("AssetId") REFERENCES "MediaAssets" ("Id") ON DELETE CASCADE
);
COMMENT ON TABLE "Metadata" IS 'EXIF data, 1:1 with MediaAsset and keyed by it — there is no separate Id. Populated by ExifExtractionMiddleware; every field is best-effort, because a file may carry no EXIF at all.';
COMMENT ON COLUMN "Metadata"."OrientationDegrees" IS 'Rotation needed to display the image upright, already normalised from the raw EXIF orientation flag so consumers never interpret the flag themselves.';


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
COMMENT ON TABLE "ProxyFiles" IS 'One row per generated artefact on the local SSD. An asset has several. Written only by ProxyStagingWriter, at the moment the file reaches its final name.';
COMMENT ON COLUMN "ProxyFiles"."ProxyPath" IS 'Path relative to the proxy root, sharded by CONTENT rather than by ingestion attempt so a retry overwrites its own debris instead of stranding it. IProxyDirectoryStrategy is the single place that rule lives.';
COMMENT ON COLUMN "ProxyFiles"."ProxyType" IS 'Thumbnail, WebVideo, FullPreview or BlurHash, persisted as text. Note BlurHash is an ordinary proxy ROW whose content is a file — not a string column on the asset.';


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


CREATE INDEX "IX_MediaAssets_StorageConfigId_ContentHash" ON "MediaAssets" ("StorageConfigId", "ContentHash");


CREATE UNIQUE INDEX "IX_MediaAssets_StorageConfigId_FilePath" ON "MediaAssets" ("StorageConfigId", "FilePath");


CREATE INDEX "IX_ProxyFiles_AssetId" ON "ProxyFiles" ("AssetId");


CREATE UNIQUE INDEX "IX_RefreshTokens_TokenHash" ON "RefreshTokens" ("TokenHash");


CREATE INDEX "IX_RefreshTokens_UserId_ExpiresAt" ON "RefreshTokens" ("UserId", "ExpiresAt");


CREATE UNIQUE INDEX "IX_StorageConfigs_RootPath" ON "StorageConfigs" ("RootPath");


CREATE INDEX "IX_StorageConfigs_UserId" ON "StorageConfigs" ("UserId");


CREATE UNIQUE INDEX ix_users_email ON "Users" ("Email");


CREATE UNIQUE INDEX ix_users_username ON "Users" ("Username");


ALTER TABLE "Bursts" ADD CONSTRAINT "FK_Bursts_MediaAssets_PrimaryAssetId" FOREIGN KEY ("PrimaryAssetId") REFERENCES "MediaAssets" ("Id") ON DELETE RESTRICT;



