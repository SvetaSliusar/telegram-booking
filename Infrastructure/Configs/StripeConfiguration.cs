namespace Telegram.Bot.Infrastructure.Configs
{
    public class CustomStripeConfiguration
    {
        public static readonly string Configuration = "StripeConfiguration";
        public string MonthlyPrice { get; set; } = string.Empty;
        public string QuarterlyPrice { get; set; } = string.Empty;
        public string YearlyPrice { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
    }
}
