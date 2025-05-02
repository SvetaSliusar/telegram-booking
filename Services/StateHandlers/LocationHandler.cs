using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class LocationHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForLocation" };

    public LocationHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<LocationHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService,
        ITranslationService translationService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService, translationService)
    {
    }

    public override bool CanHandle(string state)
    {
        return state == StateNames[0];
    }

    public override async Task HandleAsync(long chatId, string state, Message message, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        if (message.Location == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "LocationRequired"),
                cancellationToken: cancellationToken);
            return;
        }
        var company = await DbContext.Companies
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);
        if (company == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "NoCompanyFound"),
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
            return;
        }

        company.Latitude = message.Location.Latitude;
        company.Longitude = message.Location.Longitude;
        await DbContext.SaveChangesAsync(cancellationToken);

        UserStateService.RemoveConversation(chatId);
        
        await BotClient.SendMessage(
            chatId: chatId,
            text: TranslationService.Get(language, "LocationSaved"),
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: cancellationToken);
    }
} 