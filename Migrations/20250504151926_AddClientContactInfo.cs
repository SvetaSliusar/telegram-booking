using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBookingBot.Migrations
{
    /// <inheritdoc />
    public partial class AddClientContactInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Clients",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Clients");
        }
    }
}
