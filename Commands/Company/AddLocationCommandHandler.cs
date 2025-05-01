
using Telegram.Bot.Commands;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Command.Company;

public class AddLocationCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;

    public AddLocationCommandHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext)
    {
        _botClient = botClient;
        _userStateService = userStateService;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
         if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;

        await ShowLocationInput(chatId, cancellationToken);
    }

    private async Task ShowLocationInput(long chatId, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        _userStateService.SetConversation(chatId, "WaitingForLocation");

        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton(Translations.GetMessage(language, "ShareLocation")) { RequestLocation = true } }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SendLocation"),
            replyMarkup: replyKeyboard,
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

}
