using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class MetadataCommentNamesAllSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "Metadata",
                comment: "What a file records in its EXIF, XMP, IPTC, QuickTime or AVI metadata, 1:1 with MediaAsset and keyed by it — there is no separate Id. Populated by MetadataExtractionMiddleware; every field is best-effort, because a file may record none of it.",
                oldComment: "EXIF data, 1:1 with MediaAsset and keyed by it — there is no separate Id. Populated by ExifExtractionMiddleware; every field is best-effort, because a file may carry no EXIF at all.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "Metadata",
                comment: "EXIF data, 1:1 with MediaAsset and keyed by it — there is no separate Id. Populated by ExifExtractionMiddleware; every field is best-effort, because a file may carry no EXIF at all.",
                oldComment: "What a file records in its EXIF, XMP, IPTC, QuickTime or AVI metadata, 1:1 with MediaAsset and keyed by it — there is no separate Id. Populated by MetadataExtractionMiddleware; every field is best-effort, because a file may record none of it.");
        }
    }
}
