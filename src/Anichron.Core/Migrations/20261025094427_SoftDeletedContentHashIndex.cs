using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeletedContentHashIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsSoftDeleted",
                table: "MediaAssets",
                type: "boolean",
                nullable: false,
                comment: "Set by reconciliation when a complete crawl did not find the file, or when its path now holds content stored at another path. Soft rather than hard so the user's interactions survive, and a crawl that finds the file again clears it (auto-restore).",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Set by reconciliation when a file is gone from the NAS. Soft rather than hard so the user's interactions survive a temporarily unmounted share.");

            migrationBuilder.CreateIndex(
                name: "ix_mediaassets_storageconfigid_contenthash_softdeleted",
                table: "MediaAssets",
                columns: new[] { "StorageConfigId", "ContentHash" },
                filter: "\"IsSoftDeleted\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mediaassets_storageconfigid_contenthash_softdeleted",
                table: "MediaAssets");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSoftDeleted",
                table: "MediaAssets",
                type: "boolean",
                nullable: false,
                comment: "Set by reconciliation when a file is gone from the NAS. Soft rather than hard so the user's interactions survive a temporarily unmounted share.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Set by reconciliation when a complete crawl did not find the file, or when its path now holds content stored at another path. Soft rather than hard so the user's interactions survive, and a crawl that finds the file again clears it (auto-restore).");
        }
    }
}
