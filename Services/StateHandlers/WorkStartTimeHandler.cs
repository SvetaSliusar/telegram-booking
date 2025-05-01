using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class WorkStartTimeHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForWorkStartTime_" };

    public WorkStartTimeHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<WorkStartTimeHandler> logger,
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
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        
        if (!TimeSpan.TryParseExact(message, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan startTime))
        {
                await BotClient.SendMessage(
                    chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                    cancellationToken: cancellationToken);
            return;
        }

        var workingHours = await DbContext.WorkingHours
            .FirstOrDefaultAsync(wh => wh.EmployeeId == employeeId && wh.DayOfWeek == day, cancellationToken);

        SetState(chatId, $"WaitingForWorkEndTime_{employeeId}_{(int)day}_{startTime}");

        await BotClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "WorkTimeEnd"),
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), $"change_work_time") }
            }),
            cancellationToken: cancellationToken);
    }
} 