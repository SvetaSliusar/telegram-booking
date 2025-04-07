using System.Collections.Concurrent;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public class UserStateService : IUserStateService
{
    private readonly ConcurrentDictionary<long, ClientConversationState> _userStates = new();
    private readonly ConcurrentDictionary<long, string> _userLanguages = new();
    private readonly ConcurrentDictionary<long, string> _userConversations = new();

    public ClientConversationState GetOrCreate(long chatId, int companyId)
    {
        var state = _userStates.GetOrAdd(chatId, _ => new ClientConversationState
        {
            CompanyId = companyId,
            CurrentStep = ConversationStep.AwaitingLanguage
        });

        lock (state.Lock)
        {
            if (state.CompanyId != companyId)
            {
                // Reset full state if company changes
                state.CompanyId = companyId;
                state.CurrentStep = ConversationStep.AwaitingLanguage;
                state.CompanyCreationData = new CompanyCreationData();
                state.ServiceCreationData = new ServiceCreationData();
                state.CurrentStep = ConversationStep.None;
            }

            return state;
        }
    }

    public string GetLanguage(long chatId)
    {
        return _userLanguages.TryGetValue(chatId, out var language) ? language : "EN";
    }

    public void SetLanguage(long chatId, string language)
    {
        _userLanguages[chatId] = language;
    }

    public string GetConversation(long chatId)
    {
        return _userConversations.TryGetValue(chatId, out var conversation) ? conversation : string.Empty;
    }

    public void SetConversation(long chatId, string conversation)
    {
        _userConversations[chatId] = conversation;
    }

    public void RemoveConversation(long chatId)
    {
        _userConversations.TryRemove(chatId, out _);
    }
}
