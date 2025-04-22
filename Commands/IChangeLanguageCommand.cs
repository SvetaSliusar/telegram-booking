namespace Telegram.Bot.Commands;

public interface IChangeLanguageCommandHandler
{
    Task HandleSetLanguageCommandAsync(long chatId, string messageText, CancellationToken cancellationToken);
    Task HandleChangeLanguageCommandAsync(long chatId, string messageText, CancellationToken cancellationToken);
}
