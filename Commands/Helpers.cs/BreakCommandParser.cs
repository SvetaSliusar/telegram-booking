using System.Globalization;

namespace Telegram.Bot.Commands.Helpers;
public static class BreakCommandParser
{
    public static (DayOfWeek day, int breakId) ParseDayAndIdFromData(string data)
    {
        var parts = data.Split('_');
        var day = Enum.Parse<DayOfWeek>(parts[0]);
        var breakId = int.Parse(parts[1]);
        return (day, breakId);
    }

    public static (DayOfWeek day, TimeSpan startTime) ParseDayAndTimeFromData(string data)
    {
        var parts = data.Split('_');
        var day = Enum.Parse<DayOfWeek>(parts[0]);
        var startTime = TimeSpan.Parse(parts[1], CultureInfo.InvariantCulture);
        return (day, startTime);
    }

    public static (int employerId, DayOfWeek day) ParseEmployerIdAndDayFromData(string data)
    {
        var parts = data.Split('_');
        var employerId = int.Parse(parts[0]);
        var day = Enum.Parse<DayOfWeek>(parts[1]);
        return (employerId, day);
    }

    public static (DayOfWeek day, TimeSpan startTime, TimeSpan endTime) ParseDayStartEndTimeFromData(string data)
    {
        var parts = data.Split('_');
        var day = Enum.Parse<DayOfWeek>(parts[0]);
        var startTime = TimeSpan.Parse(parts[1], CultureInfo.InvariantCulture);
        var endTime = TimeSpan.Parse(parts[2], CultureInfo.InvariantCulture);
        return (day, startTime, endTime);
    }

    public static (string commandKey, string data) SplitCommandData(string rawData)
    {
        var separatorIndex = rawData.IndexOf(':');
        if (separatorIndex >= 0)
        {
            var commandKey = rawData.Substring(0, separatorIndex);
            var data = rawData.Substring(separatorIndex + 1);
            return (commandKey, data);
        }

        return (rawData, string.Empty);
    }
}