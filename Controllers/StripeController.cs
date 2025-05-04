using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;
using Telegram.Bot.Commands.Common;
using Telegram.Bot.Services;

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

    public StripeWebhookController(
        IConfiguration config,
        ILogger<StripeWebhookController> logger,
        ITokensService tokenService,
        ITelegramBotClient botClient,
        IMainMenuCommandHandler mainMenuCommandHandler,
        IUserStateService userStateService,
        ITranslationService translationService)
    {
        _config = config;
        _logger = logger;
        _tokenService = tokenService;
        _botClient = botClient;
        _mainMenuCommandHandler = mainMenuCommandHandler;
        _userStateService = userStateService;
        _translationService = translationService;
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

        if (stripeEvent.Type == "checkout.session.completed")
        {
            var session = stripeEvent.Data.Object as Stripe.Checkout.Session;

            var telegramChatId = session?.Metadata?["chat_id"];

            if (!string.IsNullOrWhiteSpace(telegramChatId) && long.TryParse(telegramChatId, out var chatId))
            {
                var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
                await _tokenService.AddCompanySetupTokenAsync(chatId);
                
                await _botClient.SendMessage(
                    chatId,
                    text: _translationService.Get(language, "ThankYouForSubscription"),
                    cancellationToken: cancellationToken
                );

                await _mainMenuCommandHandler.ShowCompanyMainMenuAsync(
                    chatId,
                    language,
                    cancellationToken
                );
            }
        }

        return Ok();
    }
}
