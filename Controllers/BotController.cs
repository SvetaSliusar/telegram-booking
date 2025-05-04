using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Commands.Common;
using Telegram.Bot.Enums;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Types;
using static Telegram.Bot.Commands.Helpers.RoleHandler;

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
    public async Task<IActionResult> Post([FromBody] Update? update, CancellationToken cancellationToken)
    {
        if (update == null)
        {
            _logger.LogWarning("Received empty update.");
            return BadRequest("Update is null.");
        }

        _logger.LogInformation("Received update: {Update}", update);

        var message = update.Message;
        var callback = update.CallbackQuery;

        var chatId = message?.Chat.Id ?? callback?.Message?.Chat.Id;

        if (message != null)
        {
            var messageText = message.Text;

            // handle /start
            if (await _startCommandHandler.HandleStartCommandAsync(message, cancellationToken))
                return Ok();

            // handle /menu
            if (messageText == "/menu")
            {
                await _mainMenuHandler.ShowMainMenuAsync(message.Chat.Id, cancellationToken);
                return Ok();
            }
        }

        if (chatId.HasValue)
        {
            var activeRole = await _companyUpdateHandler.GetModeAsync(chatId.Value, cancellationToken);
            
            switch (activeRole)
            {
                case UserRole.Company:
                    await _companyUpdateHandler.HandleUpdateAsync(update, cancellationToken);
                    break;
                case UserRole.Client:
                    await _clientUpdateHandler.HandleUpdateAsync(update, cancellationToken);
                    break;
            }
        }

        return Ok();
    }
}
