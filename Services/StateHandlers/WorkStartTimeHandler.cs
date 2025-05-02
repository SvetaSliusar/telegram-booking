using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
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
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        
        if (!TimeSpan.TryParseExact(message.Text, "hh\\:mm", CultureInfo.InvariantCulture, out TimeSpan startTime))
        {
                await BotClient.SendMessage(
                    chatId: chatId,
                    text: TranslationService.Get(language, "InvalidTimeFormat"),
                    parseMode: ParseMode.MarkdownV2,
                    cancellationToken: cancellationToken);
            return;
        }

        var workingHours = await DbContext.WorkingHours
            .FirstOrDefaultAsync(wh => wh.EmployeeId == employeeId && wh.DayOfWeek == day, cancellationToken);

        SetState(chatId, $"WaitingForWorkEndTime_{employeeId}_{(int)day}_{startTime}");

        await BotClient.SendMessage(
            chatId: chatId,
            text: TranslationService.Get(language, "WorkTimeEnd"),
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(TranslationService.Get(language, "Back"), $"change_work_time") }
            }),
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancellationToken);
    }
} 