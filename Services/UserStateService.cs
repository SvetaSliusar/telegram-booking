using System.Collections.Concurrent;
using Telegram.Bot.Enums;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public class UserStateService : IUserStateService
{
    private readonly ConcurrentDictionary<long, string> _userConversations = new();
    private readonly ConcurrentDictionary<long, int?> _lastMessageIds = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserStateService> _logger;
    private readonly ConcurrentDictionary<long, UserRole> _roleCache = new();
    private readonly ConcurrentDictionary<long, string> _languageCache = new();

    public UserStateService(
        IServiceScopeFactory scopeFactory,
        ILogger<UserStateService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    
    public async Task<UserRole> GetUserRoleAsync(long chatId, CancellationToken cancellationToken)
    {
        if (_roleCache.TryGetValue(chatId, out var cachedRole))
            return cachedRole;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            var state = await dbContext.UserStates.FindAsync(new object[] { chatId }, cancellationToken);
            var role = state?.Role ?? UserRole.Unknown;
            _roleCache.TryAdd(chatId, role);
            return role;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user role from DB for {ChatId}", chatId);
            return UserRole.Unknown;
        }
    }

    public async Task<UserRole> GetActiveRoleAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            var state = await dbContext.UserStates.FindAsync(new object[] { chatId }, cancellationToken);
            return state?.ActiveRole ?? UserRole.Unknown;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active user role from DB for {ChatId}", chatId);
            return UserRole.Unknown;
        }
    }

    public async Task SetUserRoleAsync(long chatId, UserRole newRole, CancellationToken cancellationToken)
    {
        await AddOrUpdateUserRolesAsync(chatId, newRole, setActive: false, cancellationToken);
    }

    public async Task SetActiveRoleAsync(long chatId, UserRole activeRole, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            var state = await dbContext.UserStates.FindAsync(new object[] { chatId }, cancellationToken);
            if (state != null)
            {
                state.ActiveRole = activeRole;
                dbContext.UserStates.Update(state);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting active role for {ChatId}", chatId);
        }
    }

    public async Task AddOrUpdateUserRolesAsync(long chatId, UserRole newRole, bool setActive, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            var state = await dbContext.UserStates.FindAsync(new object[] { chatId }, cancellationToken);
            if (state == null)
            {
                state = new UserState
                {
                    ChatId = chatId,
                    Role = newRole,
                    ActiveRole = setActive ? newRole : UserRole.Unknown
                };
                dbContext.UserStates.Add(state);
            }
            else
            {
                state.Role |= newRole;

                if (setActive)
                {
                    state.ActiveRole = newRole;
                }

                dbContext.UserStates.Update(state);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            _roleCache.AddOrUpdate(chatId, state.Role, (_, _) => state.Role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating roles for {ChatId}", chatId);
        }
    }

    public async Task<string> GetLanguageAsync(long chatId, CancellationToken cancellationToken)
    {
        if (_languageCache.TryGetValue(chatId, out var cachedLanguage))
            return cachedLanguage;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            var state = await dbContext.UserStates.FindAsync(new object[] { chatId }, cancellationToken);
            var language = state?.Language ?? "EN";
            _languageCache.TryAdd(chatId, language);
            return language;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving language from DB for {ChatId}", chatId);
            return "EN";
        }
    }

    public async Task SetLanguageAsync(long chatId, string language, CancellationToken cancellationToken)
    {
        _languageCache.AddOrUpdate(chatId, language, (_, _) => language);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            var state = await dbContext.UserStates.FindAsync(new object[] { chatId }, cancellationToken);
            if (state == null)
            {
                state = new UserState { ChatId = chatId, Language = language };
                dbContext.UserStates.Add(state);
            }
            else
            {
                state.Language = language;
                dbContext.UserStates.Update(state);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving language to DB for {ChatId}", chatId);
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
