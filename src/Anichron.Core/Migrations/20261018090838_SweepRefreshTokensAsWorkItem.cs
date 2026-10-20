using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class SweepRefreshTokensAsWorkItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "RefreshTokens",
                comment: "Issued and revoked by the API; expired rows are swept by the Worker's TokenCleanup work item. One of the few tables both services touch.",
                oldComment: "Issued and revoked by the API; expired rows are swept by the Worker's TokenCleanupService. One of the few tables both services touch.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "RefreshTokens",
                comment: "Issued and revoked by the API; expired rows are swept by the Worker's TokenCleanupService. One of the few tables both services touch.",
                oldComment: "Issued and revoked by the API; expired rows are swept by the Worker's TokenCleanup work item. One of the few tables both services touch.");
        }
    }
}
