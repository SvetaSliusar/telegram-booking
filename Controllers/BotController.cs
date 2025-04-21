using Microsoft.AspNetCore.Mvc;
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
    private readonly ICompanyService _companyService;
    private readonly ITelegramBotClient _botClient;

    public BotController(
        ILogger<BotController> logger,
        IStartCommandHandler startCommandHandler,
        CompanyUpdateHandler companyUpdateHandler,
        ClientUpdateHandler clientUpdateHandler,
        ICompanyService companyService,
        ITelegramBotClient botClient)
    {
        _logger = logger;
        _startCommandHandler = startCommandHandler;
        _companyUpdateHandler = companyUpdateHandler;
        _clientUpdateHandler = clientUpdateHandler;
        _companyService = companyService;
        _botClient = botClient;
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
        }
        else if (update.CallbackQuery != null)
        {
            var callbackData = update.CallbackQuery.Data;
            chatId = update.CallbackQuery.From.Id;

            if (callbackData == "choose_company")
            {
                await _companyUpdateHandler.StartCompanyFlow(chatId.Value, cancellationToken);
            }
            else if (callbackData == "choose_client")
            {
                var company = await _companyService.GetFirstCompanyAsync(cancellationToken);
                if (company != null)
                {
                    await _clientUpdateHandler.StartClientFlow(chatId.Value, company.Id, cancellationToken);
                }
                else
                {
                    await _botClient.SendMessage(
                        chatId: chatId.Value,
                        text: "❌ No companies available at the moment.",
                        cancellationToken: cancellationToken);
                }
            }
        }

        if (update.Message != null || update.CallbackQuery != null)
        {
            if (chatId.HasValue && _companyUpdateHandler.GetMode(chatId.Value) == Mode.Company)
                await _companyUpdateHandler.HandleUpdateAsync(update, cancellationToken);
            else if (chatId.HasValue && _companyUpdateHandler.GetMode(chatId.Value) == Mode.Client)
                await _clientUpdateHandler.HandleUpdateAsync(update, cancellationToken);
        }

        return Ok();
    }
}
