using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class BurstDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bursts_MediaAssets_PrimaryAssetId",
                table: "Bursts");

            migrationBuilder.AlterTable(
                name: "Bursts",
                comment: "Groups photos of one storage config taken moments apart, so a flashback shows one memory rather than nine near-identical frames. Written only by the Worker's BurstDetection work item, which also repairs bursts: one left with fewer than two active members is deleted, and so is one with no members at all.",
                oldComment: "Groups a rapid-fire sequence so a flashback shows one memory rather than nine near-identical frames. The table and schema exist; burst DETECTION is not yet implemented in the Worker, so in a running system this table is empty.");

            migrationBuilder.AlterColumn<Guid>(
                name: "PrimaryAssetId",
                table: "Bursts",
                type: "uuid",
                nullable: true,
                comment: "The stored cover, the earliest member. SetNull on delete, so deleting the cover's row (a storage config removal cascading to its assets) cannot fail on the burst; the next BurstDetection run picks a new cover or deletes the burst.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The frame chosen to represent the burst. Restrict on delete: losing the cover would leave a group that cannot be rendered.");

            migrationBuilder.CreateTable(
                name: "BurstChecks",
                columns: table => new
                {
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    IsCandidate = table.Column<bool>(type: "boolean", nullable: false, comment: "The photo has an EXIF capture time, a camera make and model, and a thumbnail; only candidates are grouped. False also when the original could not be read."),
                    CameraBurstId = table.Column<string>(type: "text", nullable: true, comment: "Apple's BurstUUID from the original's maker note: photos of one storage config sharing it form one burst. Null when the camera wrote none.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BurstChecks", x => x.AssetId);
                    table.ForeignKey(
                        name: "FK_BurstChecks_MediaAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "One row per photo burst detection has checked; no row means unchecked. Owned by the BurstDetection work item, which reads the original once when it checks a photo. A file edited in place loses its row, so it is checked again.");

            migrationBuilder.CreateIndex(
                name: "IX_BurstChecks_CameraBurstId",
                table: "BurstChecks",
                column: "CameraBurstId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bursts_MediaAssets_PrimaryAssetId",
                table: "Bursts",
                column: "PrimaryAssetId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bursts_MediaAssets_PrimaryAssetId",
                table: "Bursts");

            migrationBuilder.DropTable(
                name: "BurstChecks");

            migrationBuilder.AlterTable(
                name: "Bursts",
                comment: "Groups a rapid-fire sequence so a flashback shows one memory rather than nine near-identical frames. The table and schema exist; burst DETECTION is not yet implemented in the Worker, so in a running system this table is empty.",
                oldComment: "Groups photos of one storage config taken moments apart, so a flashback shows one memory rather than nine near-identical frames. Written only by the Worker's BurstDetection work item, which also repairs bursts: one left with fewer than two active members is deleted, and so is one with no members at all.");

            migrationBuilder.AlterColumn<Guid>(
                name: "PrimaryAssetId",
                table: "Bursts",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                comment: "The frame chosen to represent the burst. Restrict on delete: losing the cover would leave a group that cannot be rendered.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The stored cover, the earliest member. SetNull on delete, so deleting the cover's row (a storage config removal cascading to its assets) cannot fail on the burst; the next BurstDetection run picks a new cover or deletes the burst.");

            migrationBuilder.AddForeignKey(
                name: "FK_Bursts_MediaAssets_PrimaryAssetId",
                table: "Bursts",
                column: "PrimaryAssetId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
