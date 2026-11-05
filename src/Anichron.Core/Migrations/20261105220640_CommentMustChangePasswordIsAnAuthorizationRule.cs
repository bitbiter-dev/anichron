using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Anichron.Core.Migrations
{
    /// <inheritdoc />
    public partial class CommentMustChangePasswordIsAnAuthorizationRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "boolean",
                nullable: false,
                comment: "Set by an admin reset. Every protected route then answers 403 except the handful needed to change it.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Set by an admin reset. MustChangePasswordMiddleware then 403s every route except the handful needed to change it.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "boolean",
                nullable: false,
                comment: "Set by an admin reset. MustChangePasswordMiddleware then 403s every route except the handful needed to change it.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Set by an admin reset. Every protected route then answers 403 except the handful needed to change it.");
        }
    }
}
