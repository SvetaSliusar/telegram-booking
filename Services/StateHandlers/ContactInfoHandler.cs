using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class ContactInfoHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForContactInfo" };

    public ContactInfoHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<ContactInfoHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService)
    {
    }

    public override async Task HandleAsync(long chatId, string state, Message message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(message.Text))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(await UserStateService.GetLanguageAsync(chatId, cancellationToken), "ContactInfoRequired"),
                cancellationToken: cancellationToken);
            return;
        }

        var adminChatId = await DbContext.Companies
            .Where(c => c.Alias == "demo")
            .Select(c => c.Token.ChatId)
            .FirstOrDefaultAsync();
            
        await BotClient.SendMessage(
            chatId: adminChatId,
            text: $"📩 New company creation request!\n\nFrom user ID: {chatId}\nContact: {message.Text}",
            cancellationToken: cancellationToken);

        await BotClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(await UserStateService.GetLanguageAsync(chatId, cancellationToken), "NewContactThanks"),
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: cancellationToken);

        UserStateService.RemoveConversation(chatId);
    }
} 