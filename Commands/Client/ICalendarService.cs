namespace Telegram.Bot.Commands.Client;

public interface ICalendarService
{
    Task ShowCalendar(long chatId, DateTime selectedDate, CancellationToken cancellationToken);
}