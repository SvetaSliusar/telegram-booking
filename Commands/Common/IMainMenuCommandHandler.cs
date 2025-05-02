using Telegram.Bot.Types;

namespace Telegram.Bot.Commands.Common;

public interface IMainMenuCommandHandler
{
    Task ShowActiveMainMenuAsync(long chatId, CancellationToken cancellationToken);
    Task ShowMainMenuAsync(long chatId, CancellationToken cancellationToken);
    Task ShowClientMainMenuAsync(long chatId, string language, CancellationToken cancellationToken);
    Task ShowCompanyMainMenuAsync(long chatId, string language, CancellationToken cancellationToken);
    Task HandleSwitchRoleAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken);
}
