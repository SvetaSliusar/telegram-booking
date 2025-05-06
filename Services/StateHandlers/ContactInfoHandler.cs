using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Commands.Common;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class ContactInfoHandler : BaseStateHandler
{
    private readonly IMainMenuCommandHandler _mainMenuCommandHandler;
    public override List<string> StateNames => new List<string> {  "WaitingForContactInfo" };

    public ContactInfoHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<ContactInfoHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService,
        ITranslationService translationService,
        IMainMenuCommandHandler mainMenuCommandHandler)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService, translationService)
    {
        _mainMenuCommandHandler = mainMenuCommandHandler;
    }

    public override async Task HandleAsync(long chatId, string state, Message message, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        if (message.Contact != null)
        {
            var phoneNumber = message.Contact.PhoneNumber;
            var client = await DbContext.Clients.FirstOrDefaultAsync(c => c.ChatId == chatId);
            if (client == null)
            {
                await BotClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: TranslationService.Get(language, "NoClientFound"), 
                    replyMarkup: new ReplyKeyboardRemove(),
                    cancellationToken: cancellationToken);
                return;
            }

            client.PhoneNumber = phoneNumber;
            DbContext.Clients.Update(client);
            await DbContext.SaveChangesAsync(cancellationToken);
            UserStateService.RemoveConversation(chatId);
            await BotClient.SendMessage(
                chatId: message.Chat.Id,
                text: TranslationService.Get(language, "DataSaved"), 
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
            await _mainMenuCommandHandler.ShowClientMainMenuAsync(chatId, language, cancellationToken);
        } 
        else
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "ContactInfoRequired"),
                cancellationToken: cancellationToken);
        }
    }
}