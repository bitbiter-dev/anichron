using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class OneLivePhotoPerVideo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_PairedAssetId",
                table: "MediaAssets");

            migrationBuilder.AlterColumn<Guid>(
                name: "PairedAssetId",
                table: "MediaAssets",
                type: "uuid",
                nullable: true,
                comment: "Live Photo pairing: the photo points at its video. The video is part of the photo, never a memory of its own, and has at most one photo (unique): pairing hands it over, reconciliation soft-deletes it with its photo and leaves a lone copy of it alone. Without its video the photo is shown as a still photo, so SetNull on delete.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Live Photo pairing: the photo points at its video. The video is part of the photo, never a memory of its own: reconciliation soft-deletes it with its photo and leaves a lone copy of it alone. Without its video the photo is shown as a still photo, so SetNull on delete.");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_PairedAssetId",
                table: "MediaAssets",
                column: "PairedAssetId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_PairedAssetId",
                table: "MediaAssets");

            migrationBuilder.AlterColumn<Guid>(
                name: "PairedAssetId",
                table: "MediaAssets",
                type: "uuid",
                nullable: true,
                comment: "Live Photo pairing: the photo points at its video. The video is part of the photo, never a memory of its own: reconciliation soft-deletes it with its photo and leaves a lone copy of it alone. Without its video the photo is shown as a still photo, so SetNull on delete.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Live Photo pairing: the photo points at its video. The video is part of the photo, never a memory of its own, and has at most one photo (unique): pairing hands it over, reconciliation soft-deletes it with its photo and leaves a lone copy of it alone. Without its video the photo is shown as a still photo, so SetNull on delete.");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_PairedAssetId",
                table: "MediaAssets",
                column: "PairedAssetId");
        }
    }
}
