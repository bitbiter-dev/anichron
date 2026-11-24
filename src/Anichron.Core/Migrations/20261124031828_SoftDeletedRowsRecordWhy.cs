using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeletedRowsRecordWhy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsSoftDeleted",
                table: "MediaAssets",
                type: "boolean",
                nullable: false,
                comment: "Set by reconciliation when a complete crawl did not find the file, when its path now holds content stored at another path, or when its Live Photo's photo lets go of it. Soft rather than hard so the user's interactions survive, and a crawl that finds the file again clears it (auto-restore). SoftDeleteReason says which.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Set by reconciliation when a complete crawl did not find the file, or when its path now holds content stored at another path. Soft rather than hard so the user's interactions survive, and a crawl that finds the file again clears it (auto-restore).");

            migrationBuilder.AddColumn<string>(
                name: "SoftDeleteReason",
                table: "MediaAssets",
                type: "text",
                nullable: true,
                comment: "Why the row is soft-deleted, a SoftDeleteReason name. Set with IsSoftDeleted and cleared with it (CK_MediaAssets_SoftDeleteReason), and shown to the owner in the list of soft-deleted assets.");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaAssets_SoftDeleteReason",
                table: "MediaAssets",
                sql: "\"IsSoftDeleted\" = (\"SoftDeleteReason\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MediaAssets_SoftDeleteReason",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "SoftDeleteReason",
                table: "MediaAssets");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSoftDeleted",
                table: "MediaAssets",
                type: "boolean",
                nullable: false,
                comment: "Set by reconciliation when a complete crawl did not find the file, or when its path now holds content stored at another path. Soft rather than hard so the user's interactions survive, and a crawl that finds the file again clears it (auto-restore).",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Set by reconciliation when a complete crawl did not find the file, when its path now holds content stored at another path, or when its Live Photo's photo lets go of it. Soft rather than hard so the user's interactions survive, and a crawl that finds the file again clears it (auto-restore). SoftDeleteReason says which.");
        }
    }
}
