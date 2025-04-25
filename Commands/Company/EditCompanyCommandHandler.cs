using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Commands.Company;
public class EditCompanyCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;

    public EditCompanyCommandHandler(
        IUserStateService userStateService,
        ITelegramBotClient botClient)
    {
        _userStateService = userStateService;
        _botClient = botClient;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null)
            return;

        var chatId = callbackQuery.Message.Chat.Id;
        var messageId = callbackQuery.Message.MessageId;

        var language = _userStateService.GetLanguage(chatId);

        var keyboard = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>>
        {
            new()
            {
                InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "SetupWorkDays"), "setup_work_days"),
                InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ChangeWorkTime"), "change_work_time")
            },
            new()
            {
                InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ManageBreaks"), "manage_breaks")
            },
            new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "AddService"), "add_service") },
            new () { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "AddLocation"), "add_location") },
            new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ReminderSettings"), "reminder_settings") },
            new()
            {
                InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMainMenu"), "back_to_menu")
            }
        });

        await _botClient.EditMessageText(
            chatId: chatId,
            messageId: messageId,
            text: Translations.GetMessage(language, "EditCompanyMenuTitle"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}
