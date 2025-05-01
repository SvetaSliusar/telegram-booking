using System.Globalization;
using Telegram.Bot.Enums;
using Telegram.Bot.Services.Constants;

namespace Telegram.Bot.Services.StateHandlers;

public class DefaultStartTimeHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForDefaultStartTime" };

    public DefaultStartTimeHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<DefaultStartTimeHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService)
    {
    }

    public override bool CanHandle(string state)
    {
        return state.StartsWith(StateNames[0], StringComparison.OrdinalIgnoreCase);
    }

    public override async Task HandleAsync(long chatId, string state, string message, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        
        if (!TimeSpan.TryParseExact(message, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan startTime))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }

        var timezone = state.Split('_', 2)[1];
        UserStateService.SetConversation(chatId, $"WaitingForDefaultEndTime_{startTime}_{timezone}");

        await BotClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "EnterDefaultEndTime"),
            cancellationToken: cancellationToken);
    }
} 