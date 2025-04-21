using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class BreakStartTimeHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> { "WaitingForBreakStart_" };

    public BreakStartTimeHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<BreakStartTimeHandler> logger,
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

        var language = UserStateService.GetLanguage(chatId);
        
        if (!TimeSpan.TryParse(message, CultureInfo.InvariantCulture, out TimeSpan startTime))
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

        // Validate that start time is within working hours
        if (startTime < workingHours.StartTime || startTime >= workingHours.EndTime)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidBreakTime"),
                cancellationToken: cancellationToken);
            return;
        }
        UserStateService.SetConversation(chatId, $"WaitingForBreakEnd_{employeeId}_{(int)day}_{startTime}");

        await BotClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BreakEndTime"),
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), $"waiting_for_break_start:{employeeId}_{(int)day}") }
            }),
            cancellationToken: cancellationToken);
    }
}
