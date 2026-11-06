using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class CommentVideosHaveABlurhash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Blurhash",
                table: "Metadata",
                type: "text",
                nullable: true,
                comment: "BlurHash placeholder string (4x3 components), computed by the Worker from the upright image. Kept here rather than as a proxy file so a response needs no disk read for it. A video's is computed from its poster. Null for assets ingested before the column existed, which are not backfilled.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "BlurHash placeholder string (4x3 components), computed by the Worker from the upright image. Kept here rather than as a proxy file so a response needs no disk read for it. Null for videos, and for images ingested before the column existed, which are not backfilled.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Blurhash",
                table: "Metadata",
                type: "text",
                nullable: true,
                comment: "BlurHash placeholder string (4x3 components), computed by the Worker from the upright image. Kept here rather than as a proxy file so a response needs no disk read for it. Null for videos, and for images ingested before the column existed, which are not backfilled.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "BlurHash placeholder string (4x3 components), computed by the Worker from the upright image. Kept here rather than as a proxy file so a response needs no disk read for it. A video's is computed from its poster. Null for assets ingested before the column existed, which are not backfilled.");
        }
    }
}
