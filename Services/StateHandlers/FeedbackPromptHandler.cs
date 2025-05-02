using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.Bot.Services.StateHandlers;

public class FeedbackPromptHanlder : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForFeedback" };

    public FeedbackPromptHanlder(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<FeedbackPromptHanlder> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService,
        ITranslationService translationService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService, translationService)
    {
    }

    public override async Task HandleAsync(long chatId, string state, Message message, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        if (message.Text?.Length > 1000)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "FeedbackTooLong"),
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
                cancellationToken: cancellationToken);
            return;
        }
        await SaveFeedbackAsync(message.Text ?? "", company.Id);
        try
        {
            await SaveFeedbackAsync(message.Text ?? "", company.Id);
            UserStateService.RemoveConversation(chatId);
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "FeedbackThankYou"),
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving feedback");
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "FeedbackError"),
                cancellationToken: cancellationToken);
        }
    }

    private async Task SaveFeedbackAsync(string message, int companyId)
    {
        await DbContext.Feedbacks.AddAsync(new Feedback
        {
            Message = message,
            CreatedAt = DateTime.UtcNow,
            CompanyId = companyId
        });
        await DbContext.SaveChangesAsync();
    }
} 