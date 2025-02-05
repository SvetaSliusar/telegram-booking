using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Services;
using Telegram.Bot.Types;

namespace Telegram.Bot.Controllers;

[ApiController]
[Route("[controller]")]
public class BotController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(
        [FromBody] Update? update,
        [FromServices] CompanyUpdateHandler companyUpdateHandler,
        CancellationToken cancellationToken)
    {
        if (update == null)
        {
            Console.WriteLine("🚨 Received empty update.");
            return BadRequest("Update is null.");
        }

        Console.WriteLine($"✅ Received update: {update}");

        if (update.Message != null || update.CallbackQuery != null)
        {
            await companyUpdateHandler.HandleUpdateAsync(update, cancellationToken);
        }

        return Ok();
    }
}
