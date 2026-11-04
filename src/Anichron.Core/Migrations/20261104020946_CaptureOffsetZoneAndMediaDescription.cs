using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class CaptureOffsetZoneAndMediaDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Width",
                table: "Metadata",
                newName: "Description_Width");

            migrationBuilder.RenameColumn(
                name: "Orientation",
                table: "Metadata",
                newName: "Description_Orientation");

            migrationBuilder.RenameColumn(
                name: "Longitude",
                table: "Metadata",
                newName: "Description_Longitude");

            migrationBuilder.RenameColumn(
                name: "LensModel",
                table: "Metadata",
                newName: "Description_LensModel");

            migrationBuilder.RenameColumn(
                name: "Latitude",
                table: "Metadata",
                newName: "Description_Latitude");

            migrationBuilder.RenameColumn(
                name: "Height",
                table: "Metadata",
                newName: "Description_Height");

            migrationBuilder.RenameColumn(
                name: "DurationInSeconds",
                table: "Metadata",
                newName: "Description_DurationInSeconds");

            migrationBuilder.RenameColumn(
                name: "CameraModel",
                table: "Metadata",
                newName: "Description_CameraModel");

            migrationBuilder.RenameColumn(
                name: "CameraMake",
                table: "Metadata",
                newName: "Description_CameraMake");

            migrationBuilder.AddColumn<int>(
                name: "CaptureOffset",
                table: "MediaAssets",
                type: "integer",
                nullable: true,
                comment: "Seconds east of UTC that the file records with DateCaptured; NULL when it records none. Kept as recorded, even when it disagrees with CaptureZone (ADR 0015).");

            migrationBuilder.AddColumn<string>(
                name: "CaptureSource",
                table: "MediaAssets",
                type: "text",
                nullable: true,
                comment: "Which kind of source DateCaptured came from; NULL exactly when DateCaptured is.");

            migrationBuilder.AddColumn<string>(
                name: "CaptureZone",
                table: "MediaAssets",
                type: "text",
                nullable: true,
                comment: "IANA time zone at the asset's position when it was ingested, looked up offline; NULL without a position. Never changes DateCaptured or CaptureOffset.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CaptureOffset",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "CaptureSource",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "CaptureZone",
                table: "MediaAssets");

            migrationBuilder.RenameColumn(
                name: "Description_Width",
                table: "Metadata",
                newName: "Width");

            migrationBuilder.RenameColumn(
                name: "Description_Orientation",
                table: "Metadata",
                newName: "Orientation");

            migrationBuilder.RenameColumn(
                name: "Description_Longitude",
                table: "Metadata",
                newName: "Longitude");

            migrationBuilder.RenameColumn(
                name: "Description_LensModel",
                table: "Metadata",
                newName: "LensModel");

            migrationBuilder.RenameColumn(
                name: "Description_Latitude",
                table: "Metadata",
                newName: "Latitude");

            migrationBuilder.RenameColumn(
                name: "Description_Height",
                table: "Metadata",
                newName: "Height");

            migrationBuilder.RenameColumn(
                name: "Description_DurationInSeconds",
                table: "Metadata",
                newName: "DurationInSeconds");

            migrationBuilder.RenameColumn(
                name: "Description_CameraModel",
                table: "Metadata",
                newName: "CameraModel");

            migrationBuilder.RenameColumn(
                name: "Description_CameraMake",
                table: "Metadata",
                newName: "CameraMake");
        }
    }
}
