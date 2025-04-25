
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;

using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Client;

public class ChooseServiceCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ICalendarService _calendarService;
    public ChooseServiceCommandHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext,
        ICalendarService calendarService)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _dbContext = dbContext;
        _calendarService = calendarService;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;
        var (_, data) = SplitCommandData(callbackQuery.Data);

        var isParsed = int.TryParse(data, out var serviceId);

        if (!isParsed)
        {
           await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(_userStateService.GetLanguage(chatId), "InvalidCompanySelection"),
                cancellationToken: cancellationToken);
            return;
        }

        await HandleServiceSelection(chatId, serviceId, cancellationToken);
    }

    private async Task HandleServiceSelection(long chatId, int serviceId, CancellationToken cancellationToken)
    {
        var service = await _dbContext.Services
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Service not found. Please try again",
                cancellationToken: cancellationToken);
            return;
        }

        _userStateService.SetConversation(chatId, $"WaitingForDate_{serviceId}");

        await _calendarService.ShowCalendar(chatId, DateTime.UtcNow, cancellationToken);
    }
}
