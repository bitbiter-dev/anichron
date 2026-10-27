using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProxyOrphans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "WorkItems",
                comment: "Durable work for the Worker, scheduled by Workers or requested by an admin. Only IWorkQueue inserts rows; the API's only other write is an admin's cancel, and every other state change belongs to the Worker's executor. Delivery is at least once, so handlers start from scratch and converge.",
                oldComment: "Durable work for the Worker: periodic maintenance today, admin requests later. Only IWorkQueue inserts rows; the API's only other write is an admin's cancel, and every other state change belongs to the Worker's executor. Delivery is at least once, so handlers start from scratch and converge.");

            migrationBuilder.CreateTable(
                name: "ProxyOrphans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SweepId = table.Column<Guid>(type: "uuid", nullable: false, comment: "The ProxySweep work item that found the file; retention of the item purges its report."),
                    ProxyPath = table.Column<string>(type: "text", nullable: false, comment: "Path relative to the proxy root, compared as is with ProxyFiles.ProxyPath."),
                    StorageConfigId = table.Column<Guid>(type: "uuid", nullable: true, comment: "The storage config named by the file's directory, read through IProxyDirectoryStrategy; null when the path names none. No foreign key: the config may be gone already, or go later."),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false, comment: "Size at the scan. A deletion skips the file when its size has changed since."),
                    LastWriteTime = table.Column<Instant>(type: "timestamp with time zone", nullable: false, comment: "Last write time at the scan, truncated to microseconds. A deletion skips the file when it has changed since."),
                    DeletionOutcome = table.Column<string>(type: "text", nullable: true, comment: "Deleted, SkippedReferenced, SkippedChanged, Missing or Failed, persisted as text; null until a deletion processed the file. A processed orphan is never processed again."),
                    ProcessedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyOrphans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProxyOrphans_WorkItems_SweepId",
                        column: x => x.SweepId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "A file under the proxy root that a ProxySweep found unreferenced by any ProxyFiles row, soft-deleted assets' included, and last written more than an hour before the scan. Written only by the Worker; the API reads it. Nothing is deleted until an admin asks, and a ProxyDeletion re-checks each file before it removes it.");

            migrationBuilder.CreateIndex(
                name: "ix_proxyfiles_proxypath",
                table: "ProxyFiles",
                column: "ProxyPath")
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_proxyorphans_sweepid_proxypath",
                table: "ProxyOrphans",
                columns: new[] { "SweepId", "ProxyPath" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProxyOrphans");

            migrationBuilder.DropIndex(
                name: "ix_proxyfiles_proxypath",
                table: "ProxyFiles");

            migrationBuilder.AlterTable(
                name: "WorkItems",
                comment: "Durable work for the Worker: periodic maintenance today, admin requests later. Only IWorkQueue inserts rows; the API's only other write is an admin's cancel, and every other state change belongs to the Worker's executor. Delivery is at least once, so handlers start from scratch and converge.",
                oldComment: "Durable work for the Worker, scheduled by Workers or requested by an admin. Only IWorkQueue inserts rows; the API's only other write is an admin's cancel, and every other state change belongs to the Worker's executor. Delivery is at least once, so handlers start from scratch and converge.");
        }
    }
}
