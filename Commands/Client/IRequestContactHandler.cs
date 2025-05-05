using Telegram.Bot.Types;

namespace Telegram.Bot.Commands.Client;

public interface IRequestContactHandler
{
     Task HandleRequestContactAsync(long chatId, CancellationToken cancellationToken);
}