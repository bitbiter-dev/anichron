using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokensRememberTheirSuccessor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedById",
                table: "RefreshTokens",
                type: "uuid",
                nullable: true,
                comment: "The token this one was rotated into. Presenting this token within 30 seconds of RevokedAt, while that one is still active, is a lost refresh response, not theft: the successor is rotated in its place (ADR 0017).");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ReplacedById",
                table: "RefreshTokens",
                column: "ReplacedById",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_RefreshTokens_ReplacedById",
                table: "RefreshTokens",
                column: "ReplacedById",
                principalTable: "RefreshTokens",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_RefreshTokens_ReplacedById",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_ReplacedById",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ReplacedById",
                table: "RefreshTokens");
        }
    }
}
