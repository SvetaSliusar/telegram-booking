using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBookingBot.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingHoursBreakTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "BreakTime",
                table: "WorkingHours",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreakTime",
                table: "WorkingHours");
        }
    }
}
