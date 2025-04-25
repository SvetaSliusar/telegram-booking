using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Commands;

public class LeaveFeedbbackCommandHanlder : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;

    public LeaveFeedbbackCommandHanlder(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _dbContext = dbContext;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
         if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;

        await RequestUserInputAsync(chatId, cancellationToken);
    }

    private async Task RequestUserInputAsync(long chatId, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        _userStateService.SetConversation(chatId, "WaitingForFeedback");
        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "FeedbackPrompt"),
            replyMarkup: new ReplyKeyboardRemove(),
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }
}
