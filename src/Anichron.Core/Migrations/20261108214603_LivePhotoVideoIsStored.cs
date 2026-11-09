using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class LivePhotoVideoIsStored : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLivePhotoVideo",
                table: "MediaAssets",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the row is a Live Photo's video: set when a photo is paired with it, never cleared, even when the photo lets go of it or is purged. Pairing soft-deletes a video its photo lets go of, and a flagged video crawled alone follows only its photo, so it is never a memory of its own (ADR 0019).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLivePhotoVideo",
                table: "MediaAssets");
        }
    }
}
