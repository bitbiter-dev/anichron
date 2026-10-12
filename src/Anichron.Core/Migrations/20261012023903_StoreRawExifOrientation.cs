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
            migrationBuilder.AddColumn<int>(
                name: "Orientation",
                table: "Metadata",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "Raw EXIF orientation tag, 1-8 (1 = upright, also used when absent or invalid). Stored raw because 5 and 7 combine a rotation with a mirror and cannot be expressed as degrees; a reader converts, and for 5-8 swaps Width/Height. Proxies keep this tag on unrotated pixels, so do not rotate them twice.");

            migrationBuilder.Sql(
                """
                UPDATE "Metadata" SET "Orientation" = CASE "OrientationDegrees"
                    WHEN 90 THEN 6 WHEN 180 THEN 3 WHEN 270 THEN 8 ELSE 1 END;
                """);

            migrationBuilder.DropColumn(
                name: "OrientationDegrees",
                table: "Metadata");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrientationDegrees",
                table: "Metadata",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Rotation needed to display the image upright, already normalised from the raw EXIF orientation flag so consumers never interpret the flag themselves.");

            migrationBuilder.Sql(
                """
                UPDATE "Metadata" SET "OrientationDegrees" = CASE "Orientation"
                    WHEN 6 THEN 90 WHEN 3 THEN 180 WHEN 8 THEN 270 ELSE 0 END;
                """);

            migrationBuilder.DropColumn(
                name: "Orientation",
                table: "Metadata");
        }
    }
}
