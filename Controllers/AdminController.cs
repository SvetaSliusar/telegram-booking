using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
using Telegram.Bot.Commands.Company;
using Telegram.Bot.Types.Enums;

[ApiController]
[Route("[controller]")]
public class AdminController : ControllerBase
{
    private readonly ITelegramBotClient _botClient;
    private readonly ISubscriptionHandler _subscriptionHandler;
    private readonly IConfiguration _configuration;

    public AdminController(ITelegramBotClient botClient,
        ISubscriptionHandler subscriptionHandler,
        IConfiguration configuration)
    {
        _subscriptionHandler = subscriptionHandler;
        _botClient = botClient;
        _configuration = configuration;
    }

    [HttpPost("send-message")]
    public async Task<IActionResult> SendMessage([FromBody] ChatRequest request, [FromHeader(Name = "X-Api-Key")] string apiKey)
    {
        var chatId = request.ChatId;
        string message = """
            *Hey! 👋 Thanks again for your request to create a company.*

            You previously explored the bot as a *demo client*, but now we've launched the full *onboarding flow* — including subscription plans and instant setup.

            Tap a plan below to continue launching your company 🚀

            Need help? Message us at [@online_booking_support](https://t.me/online_booking_support).
            """;

        var requiredApiKey = _configuration["AdminApi:ApiKey"];
        if (apiKey != requiredApiKey)
        {
            return Unauthorized("Invalid API Key");
        }

        await _botClient.SendMessage(chatId, message, ParseMode.Markdown);
        await _subscriptionHandler.ShowSubscriptionsAsync(chatId, CancellationToken.None);
        return Ok("Message sent successfully.");
    }

    public class ChatRequest
    {
        public long ChatId { get; set; }
    }
}