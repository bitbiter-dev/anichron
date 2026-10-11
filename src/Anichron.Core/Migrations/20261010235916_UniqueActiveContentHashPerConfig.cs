using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class UniqueActiveContentHashPerConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_StorageConfigId_ContentHash",
                table: "MediaAssets");

            migrationBuilder.CreateIndex(
                name: "ix_mediaassets_storageconfigid_contenthash_active",
                table: "MediaAssets",
                columns: new[] { "StorageConfigId", "ContentHash" },
                unique: true,
                filter: "\"IsSoftDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mediaassets_storageconfigid_contenthash_active",
                table: "MediaAssets");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_StorageConfigId_ContentHash",
                table: "MediaAssets",
                columns: new[] { "StorageConfigId", "ContentHash" });
        }
    }
}
