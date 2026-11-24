using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class HeicOrientationIncludesMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Description_Orientation",
                table: "Metadata",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "EXIF orientation value, 1-8 (1 = upright, also used when absent or invalid): from the container's irot and imir for a HEIC, composed in the order its ipma lists them, the video track's rotation for a video, and the EXIF tag otherwise. Kept as an EXIF value because 5 and 7 combine a rotation with a mirror and cannot be expressed as degrees; a reader converts, and for 5-8 swaps Width/Height. Proxies are already upright and a video's player applies its own rotation, so rotate neither by it.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1,
                oldComment: "EXIF orientation value, 1-8 (1 = upright, also used when absent or invalid): from the container's irot for a HEIC, the video track's rotation for a video, and the EXIF tag otherwise. Kept as an EXIF value because 5 and 7 combine a rotation with a mirror and cannot be expressed as degrees; a reader converts, and for 5-8 swaps Width/Height. Proxies are already upright and a video's player applies its own rotation, so rotate neither by it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Description_Orientation",
                table: "Metadata",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "EXIF orientation value, 1-8 (1 = upright, also used when absent or invalid): from the container's irot for a HEIC, the video track's rotation for a video, and the EXIF tag otherwise. Kept as an EXIF value because 5 and 7 combine a rotation with a mirror and cannot be expressed as degrees; a reader converts, and for 5-8 swaps Width/Height. Proxies are already upright and a video's player applies its own rotation, so rotate neither by it.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1,
                oldComment: "EXIF orientation value, 1-8 (1 = upright, also used when absent or invalid): from the container's irot and imir for a HEIC, composed in the order its ipma lists them, the video track's rotation for a video, and the EXIF tag otherwise. Kept as an EXIF value because 5 and 7 combine a rotation with a mirror and cannot be expressed as degrees; a reader converts, and for 5-8 swaps Width/Height. Proxies are already upright and a video's player applies its own rotation, so rotate neither by it.");
        }
    }
}
