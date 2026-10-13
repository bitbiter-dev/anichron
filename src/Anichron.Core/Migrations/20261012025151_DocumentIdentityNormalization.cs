using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class DocumentIdentityNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "text",
                nullable: false,
                comment: "Always stored trimmed and lowercased: an EF value converter applies IdentityNormalizer.Normalize on every write and to every equality comparison against this column, so the case-sensitive unique index cannot hold two spellings of one name.",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "text",
                nullable: false,
                comment: "Always stored trimmed and lowercased: an EF value converter applies IdentityNormalizer.Normalize on every write and to every equality comparison against this column, so the case-sensitive unique index cannot hold two spellings of one name.",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Always stored trimmed and lowercased: an EF value converter applies IdentityNormalizer.Normalize on every write and to every equality comparison against this column, so the case-sensitive unique index cannot hold two spellings of one name.");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Always stored trimmed and lowercased: an EF value converter applies IdentityNormalizer.Normalize on every write and to every equality comparison against this column, so the case-sensitive unique index cannot hold two spellings of one name.");
        }
    }
}
