namespace Telegram.Bot.Commands;

public interface IMainMenuCommandHandler
{
    Task ShowMainMenuAsync(long chatId, CancellationToken cancellationToken);
}