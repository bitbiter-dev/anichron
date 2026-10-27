using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class CommentLivePhotoVideoIsPartOfThePhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PairedAssetId",
                table: "MediaAssets",
                type: "uuid",
                nullable: true,
                comment: "Live Photo pairing: the photo points at its video. The video is part of the photo, never a memory of its own: reconciliation soft-deletes it with its photo and leaves a lone copy of it alone. Without its video the photo is shown as a still photo, so SetNull on delete.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Live Photo pairing: the HEIC parent points at its MOV child. SetNull on delete so losing one half leaves the other a usable standalone asset.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "PairedAssetId",
                table: "MediaAssets",
                type: "uuid",
                nullable: true,
                comment: "Live Photo pairing: the HEIC parent points at its MOV child. SetNull on delete so losing one half leaves the other a usable standalone asset.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Live Photo pairing: the photo points at its video. The video is part of the photo, never a memory of its own: reconciliation soft-deletes it with its photo and leaves a lone copy of it alone. Without its video the photo is shown as a still photo, so SetNull on delete.");
        }
    }
}
