using Telegram.Bot.Enums;

namespace Telegram.Bot.Services;

public interface IUserStateService
{
    string GetConversation(long chatId);
    void SetConversation(long chatId, string conversation);
    void RemoveConversation(long chatId);

    Task<string> GetLanguageAsync(long chatId, CancellationToken cancellationToken);
    Task SetLanguageAsync(long chatId, string language, CancellationToken cancellationToken);
    void SetLastMessageId(long chatId, int messageId);
    int? GetLastMessageId(long chatId);
    void RemoveLastMessageId(long chatId);
    Task SetUserRoleAsync(long chatId, UserRole newRole, CancellationToken cancellationToken);
    Task<UserRole> GetUserRoleAsync(long chatId, CancellationToken cancellationToken);
}
