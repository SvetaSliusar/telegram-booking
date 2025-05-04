using Microsoft.Extensions.Options;
using Telegram.Bot.Infrastructure.Configs;
using Telegram.Bot.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Company;

public class SubscriptionCommandHandler : ICallbackCommand, ISubscriptionHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly ILogger<SubscriptionCommandHandler> _logger;
    private readonly ITranslationService _translationService;
    private readonly StripeConfiguration _stripeConfig;
    private readonly BotConfiguration _botConfig;

    public SubscriptionCommandHandler(
        IUserStateService userStateService, 
        ITelegramBotClient botClient,
        ILogger<SubscriptionCommandHandler> logger,
        ITranslationService translationService,
        IOptions<StripeConfiguration> options,
        IOptions<BotConfiguration> botConfig)
    {
        _logger = logger;
        _userStateService = userStateService;
        _botClient = botClient;
        _translationService = translationService;
        _stripeConfig = options.Value;
        _botConfig = botConfig.Value;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;

        var chatId = callbackQuery.Message.Chat.Id;
        var (commandKey, data) = SplitCommandData(callbackQuery.Data);

        var commandHandlers = new Dictionary<string, Func<long, string, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            { "inline_subscription", ShowSubscriptionsForInlineAsync },   
            {"subscribe", HandleChooseSubscriptionAsync }
        };

        if (commandHandlers.TryGetValue(commandKey, out var handler))
        {
            await handler(chatId, data, cancellationToken);
        }
        else
        {
            _logger.LogError("Unknown break command: {CommandKey}", commandKey);
        }
    }

    public async Task ShowSubscriptionsAsync(long chatId, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "QuickSubscribe"), "inline_subscription"),
                InlineKeyboardButton.WithUrl(_translationService.Get(language, "SeeAllPlans"), _botConfig.LearMoreUrl)
            }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: "How would you like to proceed?",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }   

    public async Task ShowSubscriptionsForInlineAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var text = _translationService.Get(language, "SubscriptionPlans");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "1m"), "subscribe_1m") },
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "3m"), "subscribe_3m") },
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "1y"), "subscribe_1y") }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: text,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleChooseSubscriptionAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        string? planId = data switch
        {
            "1m" => _stripeConfig.Montly,
            "3m" => _stripeConfig.Quartely,
            "1y" => _stripeConfig.Yearly,
            _ => null
        };

    if (planId != null)
    {
        string stripeUrl = $"{_stripeConfig.Url}{planId}";

        await _botClient.SendMessage(
            chatId: chatId,
            text: $"👉 Click to subscribe:\n{stripeUrl}",
            cancellationToken: cancellationToken);
    }
    }
}
