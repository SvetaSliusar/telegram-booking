using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Commands;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Types;

namespace Telegram.Bot.Controllers;

[ApiController]
[Route("[controller]")]
public class BotController : ControllerBase
{
    private readonly ILogger<BotController> _logger;
    private readonly IStartCommandHandler _startCommandHandler;
    private readonly CompanyUpdateHandler _companyUpdateHandler;
    private readonly ClientUpdateHandler _clientUpdateHandler;
    private readonly IMainMenuCommandHandler _mainMenuHandler;

    public BotController(
        ILogger<BotController> logger,
        IStartCommandHandler startCommandHandler,
        CompanyUpdateHandler companyUpdateHandler,
        ClientUpdateHandler clientUpdateHandler,
        IMainMenuCommandHandler mainMenuHandler)
    {
        _logger = logger;
        _startCommandHandler = startCommandHandler;
        _companyUpdateHandler = companyUpdateHandler;
        _clientUpdateHandler = clientUpdateHandler;
        _mainMenuHandler = mainMenuHandler;
    }

    [HttpPost]
    public async Task<IActionResult> Post(
        [FromBody] Update? update,
        CancellationToken cancellationToken)
    {
        if (update == null)
        {
            _logger.LogWarning("🚨 Received empty update.");
            return BadRequest("Update is null.");
        }

        _logger.LogInformation("✅ Received update: {update}", update);
        long? chatId = default;

        if (update?.Message != null)
        {
            var messageText = update.Message.Text;
            chatId = update.Message.Chat.Id;

            var result = await _startCommandHandler.HandleStartCommandAsync(messageText, chatId.Value, cancellationToken);

            if (result)
            {
                return Ok();
            }

            if (messageText == "/menu")
            {
                await _mainMenuHandler.ShowMainMenuAsync(chatId.Value, cancellationToken);
                return Ok();
            }
        }

        if (update.Message != null || update.CallbackQuery != null)
        {
            if (update.CallbackQuery != null)
            {
                chatId = update.CallbackQuery.Message.Chat.Id;
            }   

            if (chatId.HasValue && _companyUpdateHandler.GetMode(chatId.Value) == Mode.Company)
                await _companyUpdateHandler.HandleUpdateAsync(update, cancellationToken);
            else if (chatId.HasValue && _companyUpdateHandler.GetMode(chatId.Value) == Mode.Client)
                await _clientUpdateHandler.HandleUpdateAsync(update, cancellationToken);
        }

        return Ok();
    }
}
