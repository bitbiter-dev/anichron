using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class UniqueProxyPerAssetAndType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProxyFiles_AssetId",
                table: "ProxyFiles");

            migrationBuilder.CreateIndex(
                name: "ix_proxyfiles_assetid_proxytype",
                table: "ProxyFiles",
                columns: new[] { "AssetId", "ProxyType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_proxyfiles_assetid_proxytype",
                table: "ProxyFiles");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyFiles_AssetId",
                table: "ProxyFiles",
                column: "AssetId");
        }
    }
}
