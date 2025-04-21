using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;

namespace Telegram.Bot.Services.StateHandlers;

public class DefaultEndTimeHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForDefaultEndTime" };

    public DefaultEndTimeHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<DefaultEndTimeHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService)
    {
    }

    public override async Task HandleAsync(long chatId, string state, string message, CancellationToken cancellationToken)
    {
        var language = UserStateService.GetLanguage(chatId);
        
        if (!TimeSpan.TryParse(message, CultureInfo.InvariantCulture, out TimeSpan endTime))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }

        if (!state.StartsWith("WaitingForDefaultEndTime_"))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidState"),
                cancellationToken: cancellationToken);
            return;
        }

        var startTime = TimeSpan.Parse(state.Split('_')[1]);

        if (endTime <= startTime)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidWorkTime"),
                cancellationToken: cancellationToken);
            return;
        }

        var creationData = CompanyCreationStateService.GetState(chatId);
        var currentEmployee = creationData.Employees.FirstOrDefault(x => x.Id == creationData.CurrentEmployeeIndex);

        if (currentEmployee?.WorkingDays != null && currentEmployee.WorkingDays?.Count > 0)
        {
            var workingDays = currentEmployee?.WorkingDays;

            foreach (var dayOfWeek in workingDays)
            {
                var existingWorkingHour = await DbContext.WorkingHours.FirstOrDefaultAsync(w =>
                    w.EmployeeId == creationData.CurrentEmployeeIndex &&
                    w.DayOfWeek == dayOfWeek);

                if (existingWorkingHour != null)
                {
                    // Update existing entry
                    existingWorkingHour.StartTime = startTime;
                    existingWorkingHour.EndTime = endTime;
                }
                else
                {
                    // Add new entry
                    DbContext.WorkingHours.Add(new WorkingHours
                    {
                        EmployeeId = creationData.CurrentEmployeeIndex,
                        DayOfWeek = dayOfWeek,
                        StartTime = startTime,
                        EndTime = endTime
                    });
                }
            }

            await DbContext.SaveChangesAsync();
            CompanyCreationStateService.ClearWorkingDays(chatId, creationData.CurrentEmployeeIndex);
            CompanyCreationStateService.ClearWorkingHours(chatId, creationData.CurrentEmployeeIndex);

            UserStateService.RemoveConversation(chatId);

                await BotClient.SendMessage(
                    chatId: chatId,
                text: Translations.GetMessage(language, "DefaultWorkTimeSet", startTime.ToString(@"hh\:mm"), endTime.ToString(@"hh\:mm")),
                    cancellationToken: cancellationToken);

            await SendMessage(chatId, "UseMenuButton", cancellationToken);
        }
        else
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: "❌ No working hours selected. Please select working hours first.",
                cancellationToken: cancellationToken);
        }
    }
} 