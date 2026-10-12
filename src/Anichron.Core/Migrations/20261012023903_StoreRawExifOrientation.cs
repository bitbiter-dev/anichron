using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class StoreRawExifOrientation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrientationDegrees",
                table: "Metadata");

            migrationBuilder.AddColumn<int>(
                name: "Orientation",
                table: "Metadata",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "Raw EXIF orientation tag, 1-8 (1 = upright, also used when absent or invalid). Stored raw because 5 and 7 combine a rotation with a mirror and cannot be expressed as degrees; a reader converts, and for 5-8 swaps Width/Height. Proxies keep this tag on unrotated pixels, so do not rotate them twice.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Orientation",
                table: "Metadata");

            migrationBuilder.AddColumn<int>(
                name: "OrientationDegrees",
                table: "Metadata",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Rotation needed to display the image upright, already normalised from the raw EXIF orientation flag so consumers never interpret the flag themselves.");
        }
    }
}
