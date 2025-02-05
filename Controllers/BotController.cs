using Microsoft.AspNetCore.Mvc;
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
        CancellationToken cancellationToken)
    {
        if (update == null)
        {
            _logger.LogWarning("🚨 Received empty update.");
            return BadRequest("Update is null.");
        }

        _logger.LogInformation("✅ Received update: {update}", update);

        if (update.Message != null || update.CallbackQuery != null)
        {
            await companyUpdateHandler.HandleUpdateAsync(update, cancellationToken);
        }

        return Ok();
    }
}
