using Telegram.Bot.Examples.WebHook.Models;

namespace Telegram.Bot.Examples.WebHook.Services;

public class UserStateService
{
    private readonly Dictionary<long, ClientConversationState> _userStates = new();

    public ClientConversationState GetOrCreate(long chatId, int companyId)
    {
        if (!_userStates.ContainsKey(chatId))
        {
            _userStates[chatId] = new ClientConversationState 
            { 
                CompanyId = companyId,
                CurrentStep = ConversationStep.AwaitingLanguage 
            };
        }
        return _userStates[chatId];
    }

    public bool TryGetState(long chatId, out ClientConversationState state)
    {
        return _userStates.TryGetValue(chatId, out state);
    }

    public void SetState(long chatId, ClientConversationState state)
    {
        _userStates[chatId] = state;
    }

    public void RemoveState(long chatId)
    {
        _userStates.Remove(chatId);
    }
} 