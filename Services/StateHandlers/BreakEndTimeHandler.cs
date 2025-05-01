using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class BreakEndTimeHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> { "WaitingForBreakEnd_" };

    public BreakEndTimeHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<BreakEndTimeHandler> logger,
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

        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        
        if (!TimeSpan.TryParseExact(message, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan endTime))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }

        // Get working hours for validation
        var workingHours = await DbContext.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.EmployeeId == employeeId && wh.DayOfWeek == day, cancellationToken);

        if (workingHours == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoWorkingHours"),
                cancellationToken: cancellationToken);
        return;
    }

        // Validate that end time is within working hours and after start time
        if (endTime <= startTime || endTime > workingHours.EndTime)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidBreakTime"),
                cancellationToken: cancellationToken);
        return;
    }

        // Check for overlapping breaks
        if (workingHours.Breaks.Any(b => 
            (startTime >= b.StartTime && startTime < b.EndTime) ||
            (endTime > b.StartTime && endTime <= b.EndTime) ||
            (startTime <= b.StartTime && endTime >= b.EndTime)))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "BreakOverlap"),
                cancellationToken: cancellationToken);
        return;
    }

        // Add the break
        workingHours.Breaks.Add(new Break
        {
            StartTime = startTime,
            EndTime = endTime,
            WorkingHours = workingHours
        });

        await DbContext.SaveChangesAsync(cancellationToken);

        // Clear the conversation state
        UserStateService.RemoveConversation(chatId);

        await BotClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BreakAdded"),
            cancellationToken: cancellationToken);

        // Return to day breaks selection
        await HandleDayBreaksSelection(chatId, employeeId, day, cancellationToken);
    }

    private async Task HandleDayBreaksSelection(long chatId, int employeeId, DayOfWeek day, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        var employee = await DbContext.Employees
            .Include(e => e.WorkingHours)
                .ThenInclude(wh => wh.Breaks)
        .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoEmployeeFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var workingHours = employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == day);
        if (workingHours == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoWorkingHoursForDay"),
                cancellationToken: cancellationToken);
            return;
        }

        // Show current breaks and options
        var messageBuilder = new StringBuilder();
        messageBuilder.AppendLine(Translations.GetMessage(language, "CurrentBreaks"));
        
        if (workingHours.Breaks.Any())
        {
            foreach (var breakTime in workingHours.Breaks.OrderBy(b => b.StartTime))
            {
                var breakText = string.Format(Translations.GetMessage(language, "BreakFormat",
                    breakTime.StartTime.ToString(@"hh\:mm"),
                    breakTime.EndTime.ToString(@"hh\:mm")));
                messageBuilder.AppendLine(breakText);
            }
        }
        else
        {
            messageBuilder.AppendLine(Translations.GetMessage(language, "NoBreaks"));
        }

        var buttons = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    Translations.GetMessage(language, "AddBreak"),
                    $"add_break:{employeeId}_{(int)day}")
            }
        };

        if (workingHours.Breaks.Any())
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    Translations.GetMessage(language, "RemoveBreak"),
                    $"remove_break:{employeeId}_{(int)day}")
            });
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), "manage_breaks") });

        await BotClient.SendMessage(
            chatId: chatId,
            text: messageBuilder.ToString(),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }
} 