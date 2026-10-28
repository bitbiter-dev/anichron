using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class OneRunningWorkItemPerStorageConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "StorageConfigId",
                table: "WorkItems",
                type: "uuid",
                nullable: true,
                comment: "The storage config the work belongs to, or null for unscoped work. A per-user Worker claims only its user's configs and unscoped work; deleting the config deletes its items. At most one item per config is Running, across all Workers, so two attempts never touch one config's files at once: the claim passes over a busy config, and a partial unique index rejects a racing second claim.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The storage config the work belongs to, or null for unscoped work. A per-user Worker claims only its user's configs and unscoped work; deleting the config deletes its items.");

            migrationBuilder.CreateIndex(
                name: "ix_workitems_storageconfig_running",
                table: "WorkItems",
                column: "StorageConfigId",
                unique: true,
                filter: "\"Status\" = 'Running' AND \"StorageConfigId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workitems_storageconfig_running",
                table: "WorkItems");

            migrationBuilder.AlterColumn<Guid>(
                name: "StorageConfigId",
                table: "WorkItems",
                type: "uuid",
                nullable: true,
                comment: "The storage config the work belongs to, or null for unscoped work. A per-user Worker claims only its user's configs and unscoped work; deleting the config deletes its items.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The storage config the work belongs to, or null for unscoped work. A per-user Worker claims only its user's configs and unscoped work; deleting the config deletes its items. At most one item per config is Running, across all Workers, so two attempts never touch one config's files at once: the claim passes over a busy config, and a partial unique index rejects a racing second claim.");
        }
    }
}
