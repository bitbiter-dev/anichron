using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class FailingFilesBackOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngestionFailures",
                columns: table => new
                {
                    StorageConfigId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: false, comment: "The item's primary path relative to the storage config root, as MediaAssets.FilePath: a Live Photo's photo."),
                    Fingerprint = table.Column<string>(type: "text", nullable: false, comment: "Length and last-write time of each of the item's files, taken before the failed attempt read them. A crawl that sees another fingerprint tries the file at once."),
                    Failures = table.Column<int>(type: "integer", nullable: false, comment: "Consecutive crawls whose attempt failed on this fingerprint; a changed fingerprint starts again at 1."),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "The last failure's exception type and message, truncated."),
                    FirstFailedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    LastFailedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    RetryAfter = table.Column<Instant>(type: "timestamp with time zone", nullable: false, comment: "A crawl before this time skips the file while its fingerprint holds. The delay doubles with each failure, up to Worker:IngestionRetryMaxDays.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionFailures", x => new { x.StorageConfigId, x.FilePath });
                    table.ForeignKey(
                        name: "FK_IngestionFailures_StorageConfigs_StorageConfigId",
                        column: x => x.StorageConfigId,
                        principalTable: "StorageConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "A file of a storage config whose ingestion failed on its last attempted crawl. Written only by the Worker's crawl, once per crawl: a success or a complete walk that no longer finds the file deletes the row. The API never reads it, since its paths are media data.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngestionFailures");
        }
    }
}
