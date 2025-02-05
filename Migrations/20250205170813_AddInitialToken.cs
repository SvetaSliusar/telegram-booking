using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBookingBot.Migrations
{
    public partial class AddInitialToken : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT INTO public.\"Tokens\" (\"Id\", \"TokenValue\", \"Type\", \"ChatId\", \"CreatedAt\", \"Used\", \"CompanyId\", \"ClientId\") " +
                "VALUES (1, '123', 1, null, NOW(), false, null, null);"
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM public.\"Tokens\" WHERE \"TokenValue\" = 'initial-token-value';");
        }
    }
}
