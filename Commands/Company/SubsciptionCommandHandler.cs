using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using Telegram.Bot.Infrastructure.Configs;
using Telegram.Bot.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Company;

public class SubscriptionCommandHandler : ICallbackCommand, ISubscriptionHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly ILogger<SubscriptionCommandHandler> _logger;
    private readonly ITranslationService _translationService;
    private readonly CustomStripeConfiguration _stripeConfig;
    private readonly BotConfiguration _botConfig;

    public SubscriptionCommandHandler(
        IUserStateService userStateService, 
        ITelegramBotClient botClient,
        ILogger<SubscriptionCommandHandler> logger,
        ITranslationService translationService,
        IOptions<CustomStripeConfiguration> options,
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
            { "subscribe", HandleChooseSubscriptionAsync }
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
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "QuickSubscribe"), "inline_subscription")
            },
            new[]
            {
                InlineKeyboardButton.WithUrl(_translationService.Get(language, "SeeAllPlans"), _botConfig.LearMoreUrl + "?chat_id=" + chatId)
            }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "ProcessOptions"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }   

    public async Task ShowSubscriptionsForInlineAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var text = _translationService.Get(language, "SubscriptionPlans");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "1m"), "subscribe:1m") },
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "3m"), "subscribe:3m") },
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "1y"), "subscribe:1y") }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: text,
            replyMarkup: keyboard,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancellationToken);
    }

    public async Task<string?> CreateStripeSessionAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        string? priceId = data switch
        {
            "1m" => _stripeConfig.MonthlyPrice,
            "3m" => _stripeConfig.QuarterlyPrice,
            "1y" => _stripeConfig.YearlyPrice,
            _ => null
        };

        if (priceId != null)
        {
            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1
                    }
                },
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    TrialPeriodDays = 7
                },
                SuccessUrl = _botConfig.BotUrl,
                Metadata = new Dictionary<string, string>
                {
                    { "chat_id", chatId.ToString() }
                },
                CancelUrl = _botConfig.BotUrl
            };
            StripeConfiguration.ApiKey = _stripeConfig.ApiKey;

            var service = new SessionService();
            var session = await service.CreateAsync(options);
            var stripeUrl = session.Url;

            return stripeUrl;
        }
        return default;
    }

    private async Task HandleChooseSubscriptionAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var url = await CreateStripeSessionAsync(chatId, data, cancellationToken);
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "ClickToSubscribe", url),
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);
    }
}
