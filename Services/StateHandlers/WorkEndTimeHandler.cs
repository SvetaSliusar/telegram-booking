using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services.Constants;

namespace Telegram.Bot.Services.StateHandlers;

public class WorkEndTimeHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForWorkEndTime_" };

    public WorkEndTimeHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<WorkEndTimeHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService)
    {
    }

    public override bool CanHandle(string state)
    {
        return state.StartsWith(StateNames[0]);
    }

    public override async Task HandleAsync(long chatId, string state, string message, CancellationToken cancellationToken)
    {
        var parts = state.Split('_');
        var employeeId = int.Parse(parts[1]);
        var day = (DayOfWeek)int.Parse(parts[2]);
        var startTime = TimeSpan.Parse(parts[3]);

        var language = UserStateService.GetLanguage(chatId);
        
        if (!TimeSpan.TryParse(message, CultureInfo.InvariantCulture, out TimeSpan endTime))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
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
                text: Translations.GetMessage(language, "NoWorkingHours"),
                cancellationToken: cancellationToken);
            return;
        }

        if (endTime <= startTime)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidWorkTime"),
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