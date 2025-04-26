using Telegram.Bot.Enums;

namespace Telegram.Bot.Commands.Helpers;
public static class SupportedTimezoneExtensions
{
    public static string ToTimezoneId(this SupportedTimezone timezone)
    {
        return timezone switch
        {
            SupportedTimezone.Europe_London => "Europe/London",
            SupportedTimezone.Europe_Paris => "Europe/Paris",
            SupportedTimezone.Europe_Berlin => "Europe/Berlin",
            SupportedTimezone.Europe_Lisbon => "Europe/Lisbon",
            SupportedTimezone.Europe_Kyiv => "Europe/Kyiv",
            _ => throw new ArgumentOutOfRangeException(nameof(timezone), timezone, null)
        };
    }
}
