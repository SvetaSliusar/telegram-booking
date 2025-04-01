using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBookingBot.Migrations
{
    /// <inheritdoc />
    public partial class AddLanguageToToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Tokens");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Tokens");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Tokens");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Tokens",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Tokens");

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "Tokens",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Tokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
