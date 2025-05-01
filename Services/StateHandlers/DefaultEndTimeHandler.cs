using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Commands.Helpers;
using Telegram.Bot.Enums;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class DefaultEndTimeHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForDefaultEndTime_" };

    public DefaultEndTimeHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<DefaultEndTimeHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService)
    {
    }

    public override bool CanHandle(string state)
    {
        return state.StartsWith(StateNames[0]);
    }

    public override async Task HandleAsync(long chatId, string state, Message message, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        
        if (!TimeSpan.TryParseExact(message.Text, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan endTime))
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
        var timezone = Enum.Parse<SupportedTimezone>(state.Split('_', 3)[2]);
        
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
                    var employee = await DbContext.Employees.FindAsync(creationData.CurrentEmployeeIndex);
                    if (employee == null)
                    {
                        await BotClient.SendMessage(
                            chatId: chatId,
                            text: "❌ Employee not found.",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    // Add new entry
                    DbContext.WorkingHours.Add(new WorkingHours
                    {
                        EmployeeId = creationData.CurrentEmployeeIndex,
                        DayOfWeek = dayOfWeek,
                        StartTime = startTime,
                        EndTime = endTime,
                        Employee = employee,
                        Timezone = timezone.ToTimezoneId()
                    });
                }
            }

            await DbContext.SaveChangesAsync();
            CompanyCreationStateService.ClearWorkingDays(chatId, creationData.CurrentEmployeeIndex);
            CompanyCreationStateService.ClearWorkingHours(chatId, creationData.CurrentEmployeeIndex);

            UserStateService.RemoveConversation(chatId);

            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "DefaultWorkTimeSet", timezone.ToTimezoneId(), startTime.ToString(@"hh\:mm"), endTime.ToString(@"hh\:mm")),
                cancellationToken: cancellationToken);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "AddService"), "add_service") }
            });

            await BotClient.SendMessage(chatId, Translations.GetMessage(language, "TheNextStep"), replyMarkup: keyboard, cancellationToken: cancellationToken);
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