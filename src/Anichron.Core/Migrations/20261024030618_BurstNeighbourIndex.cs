using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class BurstNeighbourIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_StorageConfigId_DateCaptured",
                table: "MediaAssets",
                columns: new[] { "StorageConfigId", "DateCaptured" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_StorageConfigId_DateCaptured",
                table: "MediaAssets");
        }
    }
}
