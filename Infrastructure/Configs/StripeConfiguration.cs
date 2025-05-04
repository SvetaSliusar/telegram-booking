namespace Telegram.Bot.Infrastructure.Configs
{
    public class StripeConfiguration
    {
        public static readonly string Configuration = "StripeConfiguration";
        public string Url { get; set; } = string.Empty;
        public string Montly { get; set; } = string.Empty;
        public string Quartely { get; set; } = string.Empty;
        public string Yearly { get; set; } = string.Empty;
    }
}
