using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class CompactPreviewIsAProxyType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ProxyType",
                table: "ProxyFiles",
                type: "text",
                nullable: false,
                comment: "Thumbnail, WebVideo, FullPreview or CompactPreview, persisted as text. The BlurHash placeholder is not a proxy: it is the Metadata.Blurhash column.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Thumbnail, WebVideo or FullPreview, persisted as text. The BlurHash placeholder is not a proxy: it is the Metadata.Blurhash column.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ProxyType",
                table: "ProxyFiles",
                type: "text",
                nullable: false,
                comment: "Thumbnail, WebVideo or FullPreview, persisted as text. The BlurHash placeholder is not a proxy: it is the Metadata.Blurhash column.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Thumbnail, WebVideo, FullPreview or CompactPreview, persisted as text. The BlurHash placeholder is not a proxy: it is the Metadata.Blurhash column.");
        }
    }
}
