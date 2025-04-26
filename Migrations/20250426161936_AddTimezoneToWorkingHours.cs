using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBookingBot.Migrations
{
    /// <inheritdoc />
    public partial class AddTimezoneToWorkingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "WorkingHours",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "WorkingHours");
        }
    }
}
