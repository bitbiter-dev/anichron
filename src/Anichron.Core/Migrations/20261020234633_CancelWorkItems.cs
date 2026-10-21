using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class CancelWorkItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "WorkItems",
                comment: "Durable work for the Worker: periodic maintenance today, admin requests later. Only IWorkQueue inserts rows; the API's only other write is an admin's cancel, and every other state change belongs to the Worker's executor. Delivery is at least once, so handlers start from scratch and converge.",
                oldComment: "Durable work for the Worker: periodic maintenance today, admin requests later. Only IWorkQueue inserts rows; every later state change belongs to the Worker's executor. Delivery is at least once, so handlers start from scratch and converge.");

            migrationBuilder.AlterColumn<string>(
                name: "Result",
                table: "WorkItems",
                type: "jsonb",
                nullable: true,
                comment: "The handler's results summary, written when the item succeeds, or when it is cancelled after its handler returned one; a Cancelled item's results are partial.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "The handler's results summary, written when the item succeeds.");

            migrationBuilder.AddColumn<Instant>(
                name: "CancelRequestedAt",
                table: "WorkItems",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When an admin asked to cancel the item. A Queued item is cancelled at once; a Running one keeps running until the Worker's next heartbeat reads this and cancels its handler. A requested item is never queued again: release and lease recovery end it Cancelled.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelRequestedAt",
                table: "WorkItems");

            migrationBuilder.AlterTable(
                name: "WorkItems",
                comment: "Durable work for the Worker: periodic maintenance today, admin requests later. Only IWorkQueue inserts rows; every later state change belongs to the Worker's executor. Delivery is at least once, so handlers start from scratch and converge.",
                oldComment: "Durable work for the Worker: periodic maintenance today, admin requests later. Only IWorkQueue inserts rows; the API's only other write is an admin's cancel, and every other state change belongs to the Worker's executor. Delivery is at least once, so handlers start from scratch and converge.");

            migrationBuilder.AlterColumn<string>(
                name: "Result",
                table: "WorkItems",
                type: "jsonb",
                nullable: true,
                comment: "The handler's results summary, written when the item succeeds.",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldComment: "The handler's results summary, written when the item succeeds, or when it is cancelled after its handler returned one; a Cancelled item's results are partial.");
        }
    }
}
