using Telegram.Bot.Types;
namespace Telegram.Bot.Commands;

public interface ICallbackCommand
{
    Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken);
}
