using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class InteractionsRecordWhenHidden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Interactions");

            migrationBuilder.AddColumn<Instant>(
                name: "HiddenAt",
                table: "Interactions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the user last hid the asset; NULL while it is shown. Being set is what hidden means, so no flag can disagree with it, and it orders the user's hidden memories, newest first.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "Interactions");

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Interactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
