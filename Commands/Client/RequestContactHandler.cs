using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Commands.Common;
using Telegram.Bot.Enums;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Client;

public class RequestContactHandler : IRequestContactHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly ITranslationService _translationService;

    public RequestContactHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ITranslationService translationService)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _translationService = translationService;
    }

    public async Task HandleRequestContactAsync(long chatId, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        _userStateService.SetConversation(chatId, "WaitingForContactInfo");
        // Reply Keyboard for phone sharing (RequestContact = true)
        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton(_translationService.Get(language, "SharePhone")) { RequestContact = true } }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "UseTelegramUsername"), "share_contact_username") }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "SharePhonePrompt"), 
            replyMarkup: replyKeyboard,
            cancellationToken: cancellationToken);

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "ContactOptions"), 
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken);
    }
}
