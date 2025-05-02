using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.Bot.Services.StateHandlers;

public class WorkEndTimeHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForWorkEndTime_" };

    public WorkEndTimeHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<WorkEndTimeHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService,
        ITranslationService translationService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService, translationService)
    {
    }

    public override bool CanHandle(string state)
    {
        return state.StartsWith(StateNames[0]);
    }

    public override async Task HandleAsync(long chatId, string state, Message message, CancellationToken cancellationToken)
    {
        var messageText = message.Text ?? "";
        var parts = state.Split('_');
        var employeeId = int.Parse(parts[1]);
        var day = (DayOfWeek)int.Parse(parts[2]);
        var startTime = TimeSpan.Parse(parts[3]);

        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        
        if (!TimeSpan.TryParseExact(messageText, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan endTime))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "InvalidTimeFormat"), 
                cancellationToken: cancellationToken);
                return;
            }

        // Get working hours for validation
        var workingHours = await DbContext.WorkingHours
            .FirstOrDefaultAsync(wh => wh.EmployeeId == employeeId && wh.DayOfWeek == day, cancellationToken);

        if (workingHours == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "NoWorkingHours"),
                cancellationToken: cancellationToken);
            return;
        }

        if (endTime <= startTime)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "InvalidWorkTime"),
                cancellationToken: cancellationToken);
            return;
        }

        workingHours.StartTime = startTime;
        workingHours.EndTime = endTime;

        await DbContext.SaveChangesAsync(cancellationToken);

        // Clear the conversation state
        UserStateService.RemoveConversation(chatId);
        await SendMessage(chatId, "WorkTimeUpdated", cancellationToken);
    }
} 