namespace Telegram.Bot.Examples.WebHook.Infrastructure.Configs
{
    public class BotConfiguration
    {
        public static readonly string Configuration = "BotConfiguration";

        public string Token { get; init; } = default!;
        public string HostAddress { get; init; } = default!;
        public string Route { get; init; } = default!;
        public string Secret { get; init; } = default!;
    }
}

