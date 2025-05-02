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
    protected readonly ITranslationService TranslationService;

    protected BaseStateHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<BaseStateHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService,
        ITranslationService translationService)
    {
        BotClient = botClient;
        UserStateService = userStateService;
        Logger = logger;
        DbContext = dbContext;
        CompanyCreationStateService = companyCreationStateService;
        TranslationService = translationService;
    }

    public abstract List<string> StateNames { get; }

    public virtual bool CanHandle(string state)
    {
        return StateNames.Contains(state);
    }

    public abstract Task HandleAsync(long chatId, string state, Message message, CancellationToken cancellationToken);

    protected async Task SendMessage(long chatId, string messageKey, CancellationToken cancellationToken, params object[] args)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        await BotClient.SendMessage(
            chatId: chatId,
            text: TranslationService.Get(language, messageKey, args),
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