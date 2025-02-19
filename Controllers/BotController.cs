using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Examples.WebHook.Services;
using Telegram.Bot.Services;
using Telegram.Bot.Types;

namespace Telegram.Bot.Controllers;

[ApiController]
[Route("[controller]")]
public class BotController : ControllerBase
{
    private readonly ILogger<BotController> _logger;

    public BotController(ILogger<BotController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post(
        [FromBody] Update? update,
        [FromServices] CompanyUpdateHandler companyUpdateHandler,
        [FromServices] ClientUpdateHandler clientUpdateHandler,
        [FromServices] TokensService tokensService,
        CancellationToken cancellationToken)
    {
        if (update == null)
        {
            _logger.LogWarning("🚨 Received empty update.");
            return BadRequest("Update is null.");
        }

        _logger.LogInformation("✅ Received update: {update}", update);
        long? chatId = default;
        if (update.Message != null)
        {
            var messageText = update.Message.Text;
            chatId = update.Message.Chat.Id;

            // Check if the user is choosing a role
            if (messageText == "/start")
            {
                await  companyUpdateHandler.ShowRoleSelection(chatId.Value, cancellationToken);
                return Ok();
            }
        }
        else if (update.CallbackQuery != null)
        {
            var callbackData = update.CallbackQuery.Data;
            chatId = update.CallbackQuery.From.Id;

            if (callbackData == "choose_company")
            {
                await companyUpdateHandler.StartCompanyFlow(chatId.Value, cancellationToken);
            }
            else if (callbackData == "choose_client")
            {
               await clientUpdateHandler.StartClientFlow(chatId.Value, cancellationToken);
            }
        }

        if (update.Message != null || update.CallbackQuery != null)
        {
            if (chatId.HasValue && companyUpdateHandler.GetMode(chatId.Value) == Mode.Company)
                await companyUpdateHandler.HandleUpdateAsync(update, cancellationToken);
            else if (chatId.HasValue && companyUpdateHandler.GetMode(chatId.Value) == Mode.Client)
                await clientUpdateHandler.HandleUpdateAsync(update, cancellationToken);
        }

        return Ok();
    }
}
