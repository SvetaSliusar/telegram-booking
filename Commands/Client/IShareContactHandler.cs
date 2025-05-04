using Telegram.Bot.Types;

namespace Telegram.Bot.Commands.Client;

public interface IShareContactHandler
{
     Task HandleRequestContactAsync(long chatId, CancellationToken cancellationToken);
}