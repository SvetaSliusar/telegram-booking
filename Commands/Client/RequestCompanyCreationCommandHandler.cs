using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Commands.Company;
using Telegram.Bot.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Client;

public class RequestCompanyCreationCommandHanlder : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly IUserStateService _userStateService;
    private readonly ITranslationService _translationService;
    private readonly ISubscriptionHandler _subscriptionHandler;
    
    public RequestCompanyCreationCommandHanlder(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext,
        ITranslationService translationService,
        ISubscriptionHandler subscriptionHandler)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _userStateService = userStateService;
        _translationService = translationService;
        _subscriptionHandler = subscriptionHandler;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;
        var (commandKey, _) = SplitCommandData(callbackQuery.Data);

        var commandHandlers = new Dictionary<string, Func<long, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            { "request_company_creation", HandleRequestCompanyCreationAsync }
        };

        if (commandHandlers.TryGetValue(commandKey, out var commandHandler))
        {
            await commandHandler(chatId, cancellationToken);
        }
        else
        {
            await _botClient.SendMessage(chatId, "Unknown command.", cancellationToken: cancellationToken);
        }
    }

    private async Task HandleRequestCompanyCreationAsync(long chatId, CancellationToken cancellationToken)
    {
       await  _subscriptionHandler.ShowSubscriptionsAsync(chatId, cancellationToken);
    }
}
