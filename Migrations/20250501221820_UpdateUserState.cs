using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBookingBot.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveRole",
                table: "UserStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveRole",
                table: "UserStates");
        }
    }
}
