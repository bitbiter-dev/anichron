using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class CommentUpdatedAtMovesOnChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Instant>(
                name: "UpdatedAt",
                table: "WorkItems",
                type: "timestamp with time zone",
                nullable: false,
                comment: "Moves on every insert, claim and transition, and on a heartbeat only when the progress snapshot changed, so a reader can ask what changed since a point in time.",
                oldClrType: typeof(Instant),
                oldType: "timestamp with time zone",
                oldComment: "Moves on every insert, claim, heartbeat and transition, so a reader can ask what changed since a point in time.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Instant>(
                name: "UpdatedAt",
                table: "WorkItems",
                type: "timestamp with time zone",
                nullable: false,
                comment: "Moves on every insert, claim, heartbeat and transition, so a reader can ask what changed since a point in time.",
                oldClrType: typeof(Instant),
                oldType: "timestamp with time zone",
                oldComment: "Moves on every insert, claim and transition, and on a heartbeat only when the progress snapshot changed, so a reader can ask what changed since a point in time.");
        }
    }
}
