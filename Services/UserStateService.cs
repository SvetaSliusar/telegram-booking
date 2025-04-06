using System.Collections.Concurrent;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public class UserStateService : IUserStateService
{
    private readonly ConcurrentDictionary<long, string> _userConversations = new();
    private readonly ConcurrentDictionary<long, string> _userLanguages = new();
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

    public string GetLanguage(long chatId)
    {
        _userLanguages.TryGetValue(chatId, out var language);
        return language ?? "EN";
    }

    public void SetLanguage(long chatId, string language)
    {
        _userLanguages[chatId] = language;
    }

    public string GetConversation(long chatId)
    {
        _userConversations.TryGetValue(chatId, out var conversation);
        return conversation;
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