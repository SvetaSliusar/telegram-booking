
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Commands.Helpers;
using Telegram.Bot.Enums;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Client;

public class ChangeTimezoneCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;

    public ChangeTimezoneCommandHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _dbContext = dbContext;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;
        var (commandKey, data) = SplitCommandData(callbackQuery.Data);

        var commandHandlers = new Dictionary<string, Func<long, string, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            { "change_timezone", ShowTimezoneSelection },
            { "set_timezone", SetTimezone }
        };

        if (commandHandlers.TryGetValue(commandKey, out var commandHandler))
        {
            await commandHandler(chatId, data, cancellationToken);
        }
        else
        {
            await _botClient.SendMessage(chatId, "Unknown command.", cancellationToken: cancellationToken);
        }
    }

    private async Task ShowTimezoneSelection(long chatId, string data, CancellationToken cancellationToken)
    {
        var timezoneButtons = Enum.GetValues(typeof(SupportedTimezone))
            .Cast<SupportedTimezone>()
            .Select(tz => new[] {
                InlineKeyboardButton.WithCallbackData(tz.ToString().Replace('_', '/'), $"set_timezone:{tz.ToTimezoneId()}")
            })
            .ToArray();

        var language = _userStateService.GetLanguage(chatId);
        
        var keyboard = new InlineKeyboardMarkup(timezoneButtons.Concat(new[] 
        { 
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), "back_to_menu") }
        }));

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SelectTimezone"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task SetTimezone(long chatId, string timezone, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (client == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoClientFound"),
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            // Validate timezone
            TimeZoneInfo.FindSystemTimeZoneById(timezone);
            
            client.TimeZoneId = timezone;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "TimezoneSet", timezone),
                cancellationToken: cancellationToken);
        }
        catch (TimeZoneNotFoundException)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimezone"),
                cancellationToken: cancellationToken);
        }
    }
}
