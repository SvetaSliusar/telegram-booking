using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public interface IUserStateService
{
   string GetConversation(long chatId);
    void SetConversation(long chatId, string conversation);
   string GetLanguage(long chatId);
   void SetLanguage(long chatId, string language);
   ClientConversationState GetOrCreate(long chatId, int companyId);
   void RemoveConversation(long chatId);
}
