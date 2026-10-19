using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class ScopeWorkItemsToStorageConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StorageConfigId",
                table: "WorkItems",
                type: "uuid",
                nullable: true,
                comment: "The storage config the work belongs to, or null for unscoped work. A per-user Worker claims only its user's configs and unscoped work; deleting the config deletes its items.");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_StorageConfigId",
                table: "WorkItems",
                column: "StorageConfigId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_StorageConfigs_StorageConfigId",
                table: "WorkItems",
                column: "StorageConfigId",
                principalTable: "StorageConfigs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_StorageConfigs_StorageConfigId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_StorageConfigId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "StorageConfigId",
                table: "WorkItems");
        }
    }
}
