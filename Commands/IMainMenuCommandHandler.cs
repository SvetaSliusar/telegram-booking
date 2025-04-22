namespace Telegram.Bot.Commands;

public interface IMainMenuCommandHandler
{
    Task ShowMainMenuAsync(long chatId, CancellationToken cancellationToken);
    Task ShowClientMainMenuAsync(long chatId, string language, CancellationToken cancellationToken);
    Task ShowCompanyMainMenuAsync(long chatId, string language, CancellationToken cancellationToken);
}
