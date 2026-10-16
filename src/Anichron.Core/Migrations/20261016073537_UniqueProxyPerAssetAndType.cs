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

            migrationBuilder.AlterTable(
                name: "ProxyFiles",
                comment: "One row per generated artefact on the local SSD. An asset has several, but at most one per ProxyType, so of two attempts racing to add the same proxy only one row lands. Written only by ProxyStagingWriter, at the moment the file reaches its final name.",
                oldComment: "One row per generated artefact on the local SSD. An asset has several. Written only by ProxyStagingWriter, at the moment the file reaches its final name.");

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

            migrationBuilder.AlterTable(
                name: "ProxyFiles",
                comment: "One row per generated artefact on the local SSD. An asset has several. Written only by ProxyStagingWriter, at the moment the file reaches its final name.",
                oldComment: "One row per generated artefact on the local SSD. An asset has several, but at most one per ProxyType, so of two attempts racing to add the same proxy only one row lands. Written only by ProxyStagingWriter, at the moment the file reaches its final name.");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyFiles_AssetId",
                table: "ProxyFiles",
                column: "AssetId");
        }
    }
}
