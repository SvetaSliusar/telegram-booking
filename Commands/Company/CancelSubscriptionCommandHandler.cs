using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Telegram.Bot.Infrastructure.Configs;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.Bot.Commands.Company;

public class CancelSubscriptionCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ITranslationService _translationService;
    private readonly CustomStripeConfiguration _config;
    private readonly BotConfiguration _botConfig;

    public CancelSubscriptionCommandHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext,
        ITranslationService translationService,
        IOptions<CustomStripeConfiguration> config,
        IOptions<BotConfiguration> botConfig)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _dbContext = dbContext;
        _translationService = translationService;
        _config = config.Value;
        _botConfig = botConfig.Value;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
         if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;

        await CancelSubscription(chatId, cancellationToken);
    }

    private async Task CancelSubscription(long chatId, CancellationToken cancellationToken)
    {
        var client = new StripeClient(_config.ApiKey);
        var portalService = new Stripe.BillingPortal.SessionService(client);
        var customerId = await _dbContext.Tokens
            .Where(c => c.ChatId == chatId)
            .Select(c => c.StripeCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        if (string.IsNullOrEmpty(customerId))
        {
            await _botClient.SendMessage(
                chatId,
                text: _translationService.Get(language, "NoSubscriptionFound"),
                cancellationToken: cancellationToken
            );
            return;
        }

        var portalSession = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = customerId,
            ReturnUrl = _botConfig.BotUrl
        });
        
        var cancelText = _translationService.Get(language, "CancelSubscriptionLinkText", portalSession.Url);

        await _botClient.SendMessage(
            chatId,
            text: cancelText,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken
        );
    }
}
