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
    private readonly ITranslationService _translationService;

    public EditCompanyCommandHandler(
        IUserStateService userStateService,
        ITelegramBotClient botClient,
        ITranslationService translationService)
    {
        _userStateService = userStateService;
        _botClient = botClient;
        _translationService = translationService;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null)
            return;

        var chatId = callbackQuery.Message.Chat.Id;
        var messageId = callbackQuery.Message.MessageId;

        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

        var keyboard = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>>
        {
            new()
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "SetupWorkDays"), "setup_work_days"),
            },
            new()
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ChangeWorkTime"), "change_work_time")
            },
            new()
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ManageBreaks"), "manage_breaks")
            },
            new() { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "AddService"), "add_service") },
            new () { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "AddLocation"), "add_location") },
            new() { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ReminderSettings"), "reminder_settings") },
            new()
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "BackToMainMenu"), "back_to_menu")
            }
        });

        await _botClient.EditMessageText(
            chatId: chatId,
            messageId: messageId,
            text: _translationService.Get(language, "MainMenu"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}
