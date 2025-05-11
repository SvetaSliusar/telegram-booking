namespace Telegram.Bot.Commands.Helpers;

public static class HtmlHelper
{
    public static string HtmlEncode(string input)
    {
        return System.Net.WebUtility.HtmlEncode(input);
    }
}