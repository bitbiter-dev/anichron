using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class UndatedAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Year",
                table: "MediaAssets",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Month",
                table: "MediaAssets",
                type: "integer",
                nullable: true,
                comment: "Stored apart from DateCaptured so \"On This Day\" is an index lookup on (Month, Day) rather than an EXTRACT() scan over every row.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Stored apart from DateCaptured so \"On This Day\" is an index lookup on (Month, Day) rather than an EXTRACT() scan over every row.");

            migrationBuilder.AlterColumn<int>(
                name: "Day",
                table: "MediaAssets",
                type: "integer",
                nullable: true,
                comment: "See Month — the pair backs IX_MediaAsset_Flashback.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "See Month — the pair backs IX_MediaAsset_Flashback.");

            migrationBuilder.AlterColumn<LocalDateTime>(
                name: "DateCaptured",
                table: "MediaAssets",
                type: "timestamp without time zone",
                nullable: true,
                comment: "The wall-clock time the file records it was captured. NULL when it records none: the asset is undated, never dated by the file's last-modified time, and is left out of On This Day and burst detection (ADR 0015).",
                oldClrType: typeof(LocalDateTime),
                oldType: "timestamp without time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Year",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Month",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Stored apart from DateCaptured so \"On This Day\" is an index lookup on (Month, Day) rather than an EXTRACT() scan over every row.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "Stored apart from DateCaptured so \"On This Day\" is an index lookup on (Month, Day) rather than an EXTRACT() scan over every row.");

            migrationBuilder.AlterColumn<int>(
                name: "Day",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "See Month — the pair backs IX_MediaAsset_Flashback.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "See Month — the pair backs IX_MediaAsset_Flashback.");

            migrationBuilder.AlterColumn<LocalDateTime>(
                name: "DateCaptured",
                table: "MediaAssets",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new NodaTime.LocalDateTime(1, 1, 1, 0, 0),
                oldClrType: typeof(LocalDateTime),
                oldType: "timestamp without time zone",
                oldNullable: true,
                oldComment: "The wall-clock time the file records it was captured. NULL when it records none: the asset is undated, never dated by the file's last-modified time, and is left out of On This Day and burst detection (ADR 0015).");
        }
    }
}
