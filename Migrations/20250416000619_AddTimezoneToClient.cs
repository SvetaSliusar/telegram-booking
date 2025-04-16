﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBookingBot.Migrations
{
    /// <inheritdoc />
    public partial class AddTimezoneToClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Clients",
                type: "text",
                nullable: false,
                defaultValue: "UTC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Clients");
        }
    }
}
