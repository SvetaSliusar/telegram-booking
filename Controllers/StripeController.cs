using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using Telegram.Bot.Commands.Common;
using Telegram.Bot.Commands.Company;
using Telegram.Bot.Services;
using Telegram.Bot.Types.Enums;

namespace Telegram.Bot.Controllers;

[Route("api/stripe/webhook")]
[ApiController]
public class StripeWebhookController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<StripeWebhookController> _logger;
    private readonly ITokensService _tokenService;
    private readonly ITelegramBotClient _botClient;
    private readonly IMainMenuCommandHandler _mainMenuCommandHandler;
    private readonly IUserStateService _userStateService;
    private readonly ITranslationService _translationService;
    private readonly ICompanyService _companyService;
    private readonly ISubscriptionHandler _subscriptionHandler;

    public StripeWebhookController(
        IConfiguration config,
        ILogger<StripeWebhookController> logger,
        ITokensService tokenService,
        ITelegramBotClient botClient,
        IMainMenuCommandHandler mainMenuCommandHandler,
        IUserStateService userStateService,
        ITranslationService translationService,
        ICompanyService companyService,
        ISubscriptionHandler subscriptionHandler)
    {
        _config = config;
        _logger = logger;
        _tokenService = tokenService;
        _botClient = botClient;
        _mainMenuCommandHandler = mainMenuCommandHandler;
        _userStateService = userStateService;
        _translationService = translationService;
        _companyService = companyService;
        _subscriptionHandler = subscriptionHandler;
    }

    [HttpPost("checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateSessionRequest request)
    {
        var url = await _subscriptionHandler.CreateStripeSessionAsync(request.ChatId, request.Plan, CancellationToken.None);
        if (string.IsNullOrEmpty(url))
        {
            return BadRequest("Failed to create checkout session.");
        }
        return Ok(new { url = url });
    }

    [HttpPost]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var stripeSignature = Request.Headers["Stripe-Signature"];
        var endpointSecret = _config["StripeConfiguration:WebhookSecret"];

        Event stripeEvent;

        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, endpointSecret);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Invalid Stripe webhook signature.");
            return BadRequest();
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                _logger.LogInformation("Checkout session completed: {SessionId}", stripeEvent.Id);
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                if (session == null)
                {
                    _logger.LogError("Session is null.");
                    return BadRequest();
                }
                await HandleCheckoutAsync(session, cancellationToken);
                break;
            case EventTypes.InvoicePaymentFailed:
                _logger.LogInformation("Invoice payment failed: {InvoiceId}", stripeEvent.Id);
                var failedInvoice = stripeEvent.Data.Object as Stripe.Invoice;
                if (failedInvoice == null)
                {
                    _logger.LogError("Invoice is null.");
                    return BadRequest();
                }
                await HandleInvoicePaymentFailedAsync(failedInvoice, cancellationToken);
                break;
            case EventTypes.CustomerSubscriptionTrialWillEnd:
                _logger.LogInformation("Customer subscription trial will end: {SubscriptionId}", stripeEvent.Id);
                var trialSubscription = stripeEvent.Data.Object as Stripe.Subscription;
                if (trialSubscription == null)
                {
                    _logger.LogError("Trial subscription is null.");
                    return BadRequest();
                }
                await HandleCustomerSubscriptionTrialWillEndAsync(trialSubscription, cancellationToken);
                break;
            case EventTypes.CustomerSubscriptionUpdated:
                _logger.LogInformation("Customer subscription updated: {SubscriptionId}", stripeEvent.Id);
                var updatedSubscription = stripeEvent.Data.Object as Stripe.Subscription;
                if (updatedSubscription == null)
                {
                    _logger.LogError("Updated subscription is null.");
                    return BadRequest();
                }
                await HandleCustomerSubscriptionUpdatedAsync(updatedSubscription, cancellationToken);
                break;
            case EventTypes.CustomerSubscriptionDeleted:
                _logger.LogInformation("Customer subscription deleted: {SubscriptionId}", stripeEvent.Id);
                var deletedSubscription = stripeEvent.Data.Object as Stripe.Subscription;
                if (deletedSubscription == null)
                {
                    _logger.LogError("Deleted subscription is null.");
                    return BadRequest();
                }
                await HandleCustomerSubscriptionDeletedAsync(deletedSubscription, cancellationToken);
                break;
        }

        return Ok();
    }

    public class CreateSessionRequest
    {
        public long ChatId { get; set; }
        public string Plan { get; set; }
    }

    
    private async Task HandleCustomerSubscriptionUpdatedAsync(Stripe.Subscription subscription, CancellationToken cancellationToken)
    {
        var customerId = subscription?.CustomerId;

        if (!string.IsNullOrEmpty(customerId))
        {
            var chatId = await _tokenService.GetChatIdByCustomerIdAsync(customerId, cancellationToken);
            if (chatId != null)
            {
                if (subscription.Status == "active")
                {
                    var language = await _userStateService.GetLanguageAsync(chatId.Value, cancellationToken);
                    var message = _translationService.Get(language, "SubscriptionUpdated");

                    await _botClient.SendMessage(
                        chatId.Value,
                        text: message,
                        cancellationToken: cancellationToken
                    );
                    await _companyService.EnableCompanyAsync(chatId.Value, cancellationToken);
                }
                else if (subscription.Status == "incomplete_expired")
                {
                    await _companyService.DisableCompanyAsync(chatId.Value, cancellationToken);
                }
                else if (subscription.Status == "cancelled")
                {
                    await _companyService.DisableCompanyAsync(chatId.Value, cancellationToken);
                    var language = await _userStateService.GetLanguageAsync(chatId.Value, cancellationToken);
                    var message = _translationService.Get(language, "SubscriptionCancelled");

                    await _botClient.SendMessage(
                        chatId.Value,
                        text: message,
                        cancellationToken: cancellationToken
                    );
                }
                else
                {
                    _logger.LogInformation("Subscription updated but not active: {Status}", subscription.Status);
                }
            }
        }
    }

    private async Task HandleCustomerSubscriptionDeletedAsync(Stripe.Subscription subscription, CancellationToken cancellationToken)
    {
        var customerId = subscription?.CustomerId;

        if (!string.IsNullOrEmpty(customerId))
        {
            var chatId = await _tokenService.GetChatIdByCustomerIdAsync(customerId, cancellationToken);
            if (chatId != null)
            {
                _logger.LogInformation("Subscription cancelled for customer {CustomerId}, chat {ChatId}", customerId, chatId);

                var language = await _userStateService.GetLanguageAsync(chatId.Value, cancellationToken);
                var message = _translationService.Get(language, "SubscriptionCancelled");

                await _botClient.SendMessage(
                    chatId.Value,
                    text: message,
                    cancellationToken: cancellationToken
                );
                await _companyService.DisableCompanyAsync(chatId.Value, cancellationToken);
            }
        }
    }

    private async Task HandleCustomerSubscriptionTrialWillEndAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var trialCustomerId = subscription?.CustomerId;

        if (!string.IsNullOrEmpty(trialCustomerId))
        {
            var chatId = await _tokenService.GetChatIdByCustomerIdAsync(trialCustomerId, cancellationToken);
            if (chatId != null)
            {
                var language = await _userStateService.GetLanguageAsync(chatId.Value, cancellationToken);
                var trialMessage = _translationService.Get(language, "TrialEndingNotice");

                await _botClient.SendMessage(
                    chatId.Value,
                    text: trialMessage,
                    cancellationToken: cancellationToken
                );
            }
        }
    }

    private async Task HandleInvoicePaymentFailedAsync(Stripe.Invoice invoice, CancellationToken cancellationToken)
    {
        var failedCustomerId = invoice?.CustomerId;

        if (!string.IsNullOrEmpty(failedCustomerId))
        {
            var chatId = await _tokenService.GetChatIdByCustomerIdAsync(failedCustomerId, cancellationToken);
            if (chatId != null)
            {
                var language = await _userStateService.GetLanguageAsync(chatId.Value, cancellationToken);
                var portalUrl = await GetPortalUrlAsync(failedCustomerId);
                if (portalUrl == null)
                {
                    _logger.LogWarning("Failed to create Stripe portal session for {CustomerId}", failedCustomerId);
                    return;
                }

                await _botClient.SendMessage(
                    chatId.Value,
                    text: _translationService.Get(language, "PaymentFailedRetryMessage", portalUrl),
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken
                );
            }
        }
    }

    private async Task HandleCheckoutAsync(Stripe.Checkout.Session session, CancellationToken cancellationToken)
    {
        var telegramChatId = session?.Metadata?["chat_id"];
        var customerId = session?.CustomerId;

        if (!string.IsNullOrWhiteSpace(telegramChatId) &&
            long.TryParse(telegramChatId, out var chatId) &&
            !string.IsNullOrWhiteSpace(customerId))
        {
            var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

            var result = await _tokenService.AddCompanySetupTokenAsync(chatId, language, customerId);
            if (!result)
            {
                _logger.LogWarning("Failed to add company setup token for chat {ChatId}", chatId);
                return;
            }
            
            await _botClient.SendMessage(
                chatId,
                text: _translationService.Get(language, "ThankYouForSubscription"),
                cancellationToken: cancellationToken
            );

            var portalUrl = await GetPortalUrlAsync(customerId);
            if (portalUrl == null)
            {
                _logger.LogWarning("Failed to create Stripe portal session for {CustomerId}", customerId);
                return;
            }

            await _companyService.EnableCompanyAsync(chatId,  cancellationToken);  
            var cancelText = _translationService.Get(language, "CancelSubscriptionLinkText", portalUrl);

            await _botClient.SendMessage(
                chatId,
                text: cancelText,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken
            );

            await _mainMenuCommandHandler.ShowCompanyMainMenuAsync(
                chatId,
                language,
                cancellationToken
            );
        }
    }

    private async Task<string?> GetPortalUrlAsync(string customerId)
    {
        string? portalUrl = null;
        try
        {
            var client = new StripeClient(_config["StripeConfiguration:ApiKey"]);
            var portalService = new Stripe.BillingPortal.SessionService(client);
            var portalSession = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = _config["BotConfiguration:BotUrl"]
            });
            portalUrl = portalSession.Url;
            return portalUrl;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Stripe portal session for {CustomerId}", customerId);
            return default;
        }
    }
}
