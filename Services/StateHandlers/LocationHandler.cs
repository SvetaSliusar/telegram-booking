using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services.Constants;

namespace Telegram.Bot.Services.StateHandlers;

public class LocationHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForLocation" };

    public LocationHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<LocationHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService)
    {
    }

    public override bool CanHandle(string state)
    {
        return state == StateNames[0];
    }

    public override async Task HandleAsync(long chatId, string state, string message, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        if (string.IsNullOrWhiteSpace(message))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "LocationRequired"),
                cancellationToken: cancellationToken);
            return;
        }
        var company = await DbContext.Companies
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);
        if (company == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }

        company.Location = message;
        await DbContext.SaveChangesAsync(cancellationToken);

        UserStateService.RemoveConversation(chatId);
        await SendMessage(chatId, "LocationSaved", cancellationToken);
    }
} 