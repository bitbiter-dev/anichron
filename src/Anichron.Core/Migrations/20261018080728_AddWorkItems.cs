using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false, comment: "WorkKind, persisted as text. Picks the payload type and the handler through the registry, never through a switch."),
                    Payload = table.Column<string>(type: "jsonb", nullable: false, comment: "The kind's IWorkPayload, serialised with System.Text.Json and NodaTime settings."),
                    DedupKey = table.Column<string>(type: "text", nullable: true, comment: "Opt-in coalescing. While an item with this key is Queued, enqueueing the same key returns that item instead of adding a second; the partial unique index enforces it."),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true, comment: "The admin who asked for the work; null when a Worker's scheduler enqueued it."),
                    RequestedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false, comment: "Queued, Running, Succeeded, Failed or Cancelled, persisted as text."),
                    Attempt = table.Column<int>(type: "integer", nullable: false, comment: "Incremented by every claim. Every write a Worker makes while running an item is fenced by (Id, Attempt), so an attempt that lost its lease changes nothing."),
                    Stalls = table.Column<int>(type: "integer", nullable: false, comment: "Lease expiries, counted by lease recovery. A graceful shutdown hands the item back without counting one; the item fails as stalled once the limit is reached."),
                    ClaimedBy = table.Column<string>(type: "text", nullable: true, comment: "The claiming Worker for display: its WORKER__USER or 'all', then its host name."),
                    StartedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<Instant>(type: "timestamp with time zone", nullable: true, comment: "Extended by every heartbeat. A Running item past it belongs to a dead or paused Worker and goes back to the queue."),
                    FinishedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false, comment: "Moves on every insert, claim, heartbeat and transition, so a reader can ask what changed since a point in time."),
                    ProgressDone = table.Column<long>(type: "bigint", nullable: false),
                    ProgressTotal = table.Column<long>(type: "bigint", nullable: true),
                    ProgressNote = table.Column<string>(type: "text", nullable: true, comment: "The latest progress snapshot (done, optional total, note), overwritten by each heartbeat; no history is kept."),
                    Result = table.Column<string>(type: "jsonb", nullable: true, comment: "The handler's results summary, written when the item succeeds."),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItems_Users_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                },
                comment: "Durable work for the Worker: periodic maintenance today, admin requests later. Only IWorkQueue inserts rows; every later state change belongs to the Worker's executor. Delivery is at least once, so handlers start from scratch and converge.");

            migrationBuilder.CreateIndex(
                name: "ix_workitems_claim",
                table: "WorkItems",
                column: "RequestedAt",
                filter: "\"Status\" = 'Queued'");

            migrationBuilder.CreateIndex(
                name: "ix_workitems_dedupkey_queued",
                table: "WorkItems",
                column: "DedupKey",
                unique: true,
                filter: "\"Status\" = 'Queued' AND \"DedupKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_workitems_lease",
                table: "WorkItems",
                column: "LeaseUntil",
                filter: "\"Status\" = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_RequestedBy",
                table: "WorkItems",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_UpdatedAt",
                table: "WorkItems",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkItems");
        }
    }
}
