using System.Collections.Concurrent;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public class UserStateService : IUserStateService
{
    private readonly ConcurrentDictionary<long, ClientConversationState> _userStates = new();
    private readonly ConcurrentDictionary<long, string> _userLanguages = new();
    private readonly ConcurrentDictionary<long, string> _userConversations = new();
    private readonly ConcurrentDictionary<long, int?> _lastMessageIds = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserStateService> _logger;

    public UserStateService(
        IServiceScopeFactory scopeFactory,
        ILogger<UserStateService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string GetState(long chatId)
    {
        return _userStates.GetValueOrDefault(chatId)?.CurrentConversation ?? "START";
    }

    public void SetState(long chatId, string state)
    {
        var userState = _userStates.GetOrAdd(chatId, _ => new ClientConversationState());
        userState.CurrentConversation = state;
    }

    public string GetLanguage(long chatId)
    {
        if (_userLanguages.TryGetValue(chatId, out var language))
        {
            return language;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            var token = dbContext.Tokens
                .FirstOrDefault(t => t.ChatId == chatId);

            if (token?.Language != null)
            {
                _userLanguages.TryAdd(chatId, token.Language);
                return token.Language;
            }

            if (token == null)
            {
                var client = dbContext.Clients
                    .FirstOrDefault(c => c.ChatId == chatId);
                if (client?.Language != null)
                {
                    _userLanguages.TryAdd(chatId, client.Language);
                    return client.Language;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting language from database for chat {ChatId}", chatId);
        }

        return "EN";
    }

    public void SetLanguage(long chatId, string language)
    {
        _userLanguages.AddOrUpdate(chatId, language, (_, _) => language);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            var token = dbContext.Tokens
                .FirstOrDefault(t => t.ChatId == chatId);

            if (token != null)
            {
                token.Language = language;
                dbContext.SaveChanges();
            }
            else
            {
                var client = dbContext.Clients
                    .FirstOrDefault(c => c.ChatId == chatId);
                if (client != null) 
                {
                    client.Language = language;
                    dbContext.SaveChanges();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving language to database for chat {ChatId}", chatId);
        }
    }

    public string GetConversation(long chatId)
    {
        return _userConversations.GetValueOrDefault(chatId, string.Empty);
    }

    public void SetConversation(long chatId, string conversation)
    {
        _userConversations.AddOrUpdate(chatId, conversation, (_, _) => conversation);
    }

    public void RemoveConversation(long chatId)
    {
        _userConversations.TryRemove(chatId, out _);
    }

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
                state.CompanyCreationData = new CompanyCreationData
                {
                    CompanyName = string.Empty,
                    CompanyAlias = string.Empty
                };
                state.ServiceCreationData = new ServiceCreationData
                {
                    Name = string.Empty,
                    Description = string.Empty
                };
                state.CurrentStep = ConversationStep.None;
            }

            return state;
        }
    }

    public int? GetLastMessageId(long chatId)
    {
        return _lastMessageIds.GetValueOrDefault(chatId);
    }

    public void SetLastMessageId(long chatId, int messageId)
    {
        _lastMessageIds.AddOrUpdate(chatId, messageId, (_, _) => messageId);
    }

    public void RemoveLastMessageId(long chatId)
    {
        _lastMessageIds.TryRemove(chatId, out _);
    }
}
