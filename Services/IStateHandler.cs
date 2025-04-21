using Telegram.Bot.Types;

namespace Telegram.Bot.Services;

public interface IStateHandler
{
    List<string> StateNames { get; }
    bool CanHandle(string state);
    Task HandleAsync(long chatId, string state, string message, CancellationToken cancellationToken);
} 