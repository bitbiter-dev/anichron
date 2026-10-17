using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class BlurhashInMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"ProxyFiles\" WHERE \"ProxyType\" = 'BlurHash';");

            migrationBuilder.AlterColumn<string>(
                name: "ProxyType",
                table: "ProxyFiles",
                type: "text",
                nullable: false,
                comment: "Thumbnail, WebVideo or FullPreview, persisted as text. The BlurHash placeholder is not a proxy: it is the Metadata.Blurhash column.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Thumbnail, WebVideo, FullPreview or BlurHash, persisted as text. Note BlurHash is an ordinary proxy ROW whose content is a file — not a string column on the asset.");

            migrationBuilder.AddColumn<string>(
                name: "Blurhash",
                table: "Metadata",
                type: "text",
                nullable: true,
                comment: "BlurHash placeholder string (4x3 components), computed by the Worker from the upright image. Kept here rather than as a proxy file so a response needs no disk read for it. Null for videos, and for images ingested before the column existed, which are not backfilled.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Blurhash",
                table: "Metadata");

            migrationBuilder.AlterColumn<string>(
                name: "ProxyType",
                table: "ProxyFiles",
                type: "text",
                nullable: false,
                comment: "Thumbnail, WebVideo, FullPreview or BlurHash, persisted as text. Note BlurHash is an ordinary proxy ROW whose content is a file — not a string column on the asset.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Thumbnail, WebVideo or FullPreview, persisted as text. The BlurHash placeholder is not a proxy: it is the Metadata.Blurhash column.");
        }
    }
}
