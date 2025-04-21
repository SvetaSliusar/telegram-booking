using Telegram.Bot.Types;
using Telegram.Bot.Services.Constants;

namespace Telegram.Bot.Services;

public abstract class BaseStateHandler : IStateHandler
{
    protected readonly ITelegramBotClient BotClient;
    protected readonly IUserStateService UserStateService;
    protected readonly ILogger<BaseStateHandler> Logger;
    protected readonly BookingDbContext DbContext;
    protected readonly ICompanyCreationStateService CompanyCreationStateService;

    protected BaseStateHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<BaseStateHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService)
    {
        BotClient = botClient;
        UserStateService = userStateService;
        Logger = logger;
        DbContext = dbContext;
        CompanyCreationStateService = companyCreationStateService;
    }

    public abstract List<string> StateNames { get; }

    public virtual bool CanHandle(string state)
    {
        return StateNames.Contains(state);
    }

    public abstract Task HandleAsync(long chatId, string state, string message, CancellationToken cancellationToken);

    protected async Task SendMessage(long chatId, string messageKey, CancellationToken cancellationToken, params object[] args)
    {
        var language = UserStateService.GetLanguage(chatId);
        await BotClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, messageKey, args),
            cancellationToken: cancellationToken);
    }

    protected void SetState(long chatId, string state)
    {
        UserStateService.SetConversation(chatId, state);
    }

    protected void ClearState(long chatId)
    {
        UserStateService.RemoveConversation(chatId);
    }
} 