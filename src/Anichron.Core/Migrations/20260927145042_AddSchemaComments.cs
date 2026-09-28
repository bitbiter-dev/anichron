using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSchemaComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "Users",
                comment: "Owns storage configs and interactions. All identity writes happen in the API; the Worker only reads.");

            migrationBuilder.AlterTable(
                name: "StorageConfigs",
                comment: "A NAS root assigned to one user. This is the boundary that scopes assets to a user and lets several Workers each monitor a separate config.");

            migrationBuilder.AlterTable(
                name: "RefreshTokens",
                comment: "Issued and revoked by the API; expired rows are swept by the Worker's TokenCleanupService. One of the few tables both services touch.");

            migrationBuilder.AlterTable(
                name: "ProxyFiles",
                comment: "One row per generated artefact on the local SSD. An asset has several. Written only by ProxyStagingWriter, at the moment the file reaches its final name.");

            migrationBuilder.AlterTable(
                name: "Metadata",
                comment: "EXIF data, 1:1 with MediaAsset and keyed by it — there is no separate Id. Populated by ExifExtractionMiddleware; every field is best-effort, because a file may carry no EXIF at all.");

            migrationBuilder.AlterTable(
                name: "MediaAssets",
                comment: "One row per media file, scoped to a storage config. Written ONLY by the Worker — the API never writes media data.");

            migrationBuilder.AlterTable(
                name: "Invites",
                comment: "Admin-issued registration tokens. Registration requires one; the first user (bootstrap admin) is exempt. Redemption is a read-then-write race, which is why this is the only table carrying a concurrency token.");

            migrationBuilder.AlterTable(
                name: "Interactions",
                comment: "Per-user state for one asset. Never shared between users: two people looking at the same file each get their own row. Drives flashback weighting — hidden excludes, starred always includes and sorts first, liked raises DisplayWeight.");

            migrationBuilder.AlterTable(
                name: "Bursts",
                comment: "Groups a rapid-fire sequence so a flashback shows one memory rather than nine near-identical frames. The table and schema exist; burst DETECTION is not yet implemented in the Worker, so in a running system this table is empty.");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "text",
                nullable: false,
                comment: "Argon2id. The parameters live with the hasher, not here, so rotating them does not require a migration.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "boolean",
                nullable: false,
                comment: "Set by an admin reset. MustChangePasswordMiddleware then 403s every route except the handful needed to change it.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "RootPath",
                table: "StorageConfigs",
                type: "text",
                nullable: false,
                comment: "GLOBALLY unique, not per user: a path cannot be assigned to two configs across all users. Two users indexing the same directory would duplicate every asset and let one user's config removal cascade away the other's rows.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TokenHash",
                table: "RefreshTokens",
                type: "text",
                nullable: false,
                comment: "Hash, never the token itself — a database disclosure must not hand out usable sessions.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ProxyType",
                table: "ProxyFiles",
                type: "text",
                nullable: false,
                comment: "Thumbnail, WebVideo, FullPreview or BlurHash, persisted as text. Note BlurHash is an ordinary proxy ROW whose content is a file — not a string column on the asset.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ProxyPath",
                table: "ProxyFiles",
                type: "text",
                nullable: false,
                comment: "Path relative to the proxy root, sharded by CONTENT rather than by ingestion attempt so a retry overwrites its own debris instead of stranding it. IProxyDirectoryStrategy is the single place that rule lives.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<int>(
                name: "OrientationDegrees",
                table: "Metadata",
                type: "integer",
                nullable: false,
                comment: "Rotation needed to display the image upright, already normalised from the raw EXIF orientation flag so consumers never interpret the flag themselves.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "PairedAssetId",
                table: "MediaAssets",
                type: "uuid",
                nullable: true,
                comment: "Live Photo pairing: the HEIC parent points at its MOV child. SetNull on delete so losing one half leaves the other a usable standalone asset.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Month",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "Stored apart from DateCaptured so \"On This Day\" is an index lookup on (Month, Day) rather than an EXTRACT() scan over every row.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSoftDeleted",
                table: "MediaAssets",
                type: "boolean",
                nullable: false,
                comment: "Set by reconciliation when a file is gone from the NAS. Soft rather than hard so the user's interactions survive a temporarily unmounted share.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "MediaAssets",
                type: "text",
                nullable: false,
                comment: "Path relative to the storage config root. Not to be confused with ProxyFiles.ProxyPath, which is relative to the proxy root.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<int>(
                name: "Day",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "See Month — the pair backs IX_MediaAsset_Flashback.",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "ContentHash",
                table: "MediaAssets",
                type: "text",
                nullable: false,
                comment: "XXHash64 of the file contents. Dedup is deliberately CONFIG-SCOPED: the same bytes under two storage configs are two assets, which is what makes multi-user work. A global unique index on this column alone would break that and must never be added.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TokenHash",
                table: "Invites",
                type: "character varying(44)",
                maxLength: 44,
                nullable: false,
                comment: "Hash, never the token itself. 44 chars — base64 of a 32-byte value.",
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44);

            migrationBuilder.AlterColumn<Guid>(
                name: "PrimaryAssetId",
                table: "Bursts",
                type: "uuid",
                nullable: false,
                comment: "The frame chosen to represent the burst. Restrict on delete: losing the cover would leave a group that cannot be rendered.",
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "Users",
                oldComment: "Owns storage configs and interactions. All identity writes happen in the API; the Worker only reads.");

            migrationBuilder.AlterTable(
                name: "StorageConfigs",
                oldComment: "A NAS root assigned to one user. This is the boundary that scopes assets to a user and lets several Workers each monitor a separate config.");

            migrationBuilder.AlterTable(
                name: "RefreshTokens",
                oldComment: "Issued and revoked by the API; expired rows are swept by the Worker's TokenCleanupService. One of the few tables both services touch.");

            migrationBuilder.AlterTable(
                name: "ProxyFiles",
                oldComment: "One row per generated artefact on the local SSD. An asset has several. Written only by ProxyStagingWriter, at the moment the file reaches its final name.");

            migrationBuilder.AlterTable(
                name: "Metadata",
                oldComment: "EXIF data, 1:1 with MediaAsset and keyed by it — there is no separate Id. Populated by ExifExtractionMiddleware; every field is best-effort, because a file may carry no EXIF at all.");

            migrationBuilder.AlterTable(
                name: "MediaAssets",
                oldComment: "One row per media file, scoped to a storage config. Written ONLY by the Worker — the API never writes media data.");

            migrationBuilder.AlterTable(
                name: "Invites",
                oldComment: "Admin-issued registration tokens. Registration requires one; the first user (bootstrap admin) is exempt. Redemption is a read-then-write race, which is why this is the only table carrying a concurrency token.");

            migrationBuilder.AlterTable(
                name: "Interactions",
                oldComment: "Per-user state for one asset. Never shared between users: two people looking at the same file each get their own row. Drives flashback weighting — hidden excludes, starred always includes and sorts first, liked raises DisplayWeight.");

            migrationBuilder.AlterTable(
                name: "Bursts",
                oldComment: "Groups a rapid-fire sequence so a flashback shows one memory rather than nine near-identical frames. The table and schema exist; burst DETECTION is not yet implemented in the Worker, so in a running system this table is empty.");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Argon2id. The parameters live with the hasher, not here, so rotating them does not require a migration.");

            migrationBuilder.AlterColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Set by an admin reset. MustChangePasswordMiddleware then 403s every route except the handful needed to change it.");

            migrationBuilder.AlterColumn<string>(
                name: "RootPath",
                table: "StorageConfigs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "GLOBALLY unique, not per user: a path cannot be assigned to two configs across all users. Two users indexing the same directory would duplicate every asset and let one user's config removal cascade away the other's rows.");

            migrationBuilder.AlterColumn<string>(
                name: "TokenHash",
                table: "RefreshTokens",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Hash, never the token itself — a database disclosure must not hand out usable sessions.");

            migrationBuilder.AlterColumn<string>(
                name: "ProxyType",
                table: "ProxyFiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Thumbnail, WebVideo, FullPreview or BlurHash, persisted as text. Note BlurHash is an ordinary proxy ROW whose content is a file — not a string column on the asset.");

            migrationBuilder.AlterColumn<string>(
                name: "ProxyPath",
                table: "ProxyFiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Path relative to the proxy root, sharded by CONTENT rather than by ingestion attempt so a retry overwrites its own debris instead of stranding it. IProxyDirectoryStrategy is the single place that rule lives.");

            migrationBuilder.AlterColumn<int>(
                name: "OrientationDegrees",
                table: "Metadata",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Rotation needed to display the image upright, already normalised from the raw EXIF orientation flag so consumers never interpret the flag themselves.");

            migrationBuilder.AlterColumn<Guid>(
                name: "PairedAssetId",
                table: "MediaAssets",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Live Photo pairing: the HEIC parent points at its MOV child. SetNull on delete so losing one half leaves the other a usable standalone asset.");

            migrationBuilder.AlterColumn<int>(
                name: "Month",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Stored apart from DateCaptured so \"On This Day\" is an index lookup on (Month, Day) rather than an EXTRACT() scan over every row.");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSoftDeleted",
                table: "MediaAssets",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Set by reconciliation when a file is gone from the NAS. Soft rather than hard so the user's interactions survive a temporarily unmounted share.");

            migrationBuilder.AlterColumn<string>(
                name: "FilePath",
                table: "MediaAssets",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Path relative to the storage config root. Not to be confused with ProxyFiles.ProxyPath, which is relative to the proxy root.");

            migrationBuilder.AlterColumn<int>(
                name: "Day",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "See Month — the pair backs IX_MediaAsset_Flashback.");

            migrationBuilder.AlterColumn<string>(
                name: "ContentHash",
                table: "MediaAssets",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "XXHash64 of the file contents. Dedup is deliberately CONFIG-SCOPED: the same bytes under two storage configs are two assets, which is what makes multi-user work. A global unique index on this column alone would break that and must never be added.");

            migrationBuilder.AlterColumn<string>(
                name: "TokenHash",
                table: "Invites",
                type: "character varying(44)",
                maxLength: 44,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44,
                oldComment: "Hash, never the token itself. 44 chars — base64 of a 32-byte value.");

            migrationBuilder.AlterColumn<Guid>(
                name: "PrimaryAssetId",
                table: "Bursts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The frame chosen to represent the burst. Restrict on delete: losing the cover would leave a group that cannot be rendered.");
        }
    }
}
