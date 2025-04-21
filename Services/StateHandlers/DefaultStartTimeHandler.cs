using System.Globalization;
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

    public override async Task HandleAsync(long chatId, string state, string message, CancellationToken cancellationToken)
    {
        var language = UserStateService.GetLanguage(chatId);
        
        if (!TimeSpan.TryParse(message, CultureInfo.InvariantCulture, out TimeSpan startTime))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }

        UserStateService.SetConversation(chatId, $"WaitingForDefaultEndTime_{startTime}");

        await BotClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "EnterDefaultEndTime"),
            cancellationToken: cancellationToken);
    }
} 