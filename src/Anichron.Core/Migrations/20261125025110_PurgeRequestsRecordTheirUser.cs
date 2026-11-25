using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class PurgeRequestsRecordTheirUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedBy",
                table: "WorkItems",
                type: "uuid",
                nullable: true,
                comment: "The user who asked for the work: an admin, or the owner of the assets a purge forgets; null when a Worker's scheduler enqueued it.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The admin who asked for the work; null when a Worker's scheduler enqueued it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "RequestedBy",
                table: "WorkItems",
                type: "uuid",
                nullable: true,
                comment: "The admin who asked for the work; null when a Worker's scheduler enqueued it.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The user who asked for the work: an admin, or the owner of the assets a purge forgets; null when a Worker's scheduler enqueued it.");
        }
    }
}
