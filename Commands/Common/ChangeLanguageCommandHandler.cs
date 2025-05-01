using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Common;
public class ChangeLanguageCommandHandler : ICallbackCommand, IChangeLanguageCommandHandler
{
    private readonly IUserStateService _userStateService;
    private readonly ITelegramBotClient _botClient;
    private readonly IMainMenuCommandHandler _mainMenuHandler;

    public ChangeLanguageCommandHandler(
        IUserStateService userStateService,
        ITelegramBotClient botClient,
        IMainMenuCommandHandler mainMenuHandler)
    {
        _userStateService = userStateService;
        _botClient = botClient;
        _mainMenuHandler = mainMenuHandler;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;

        var (commandKey, data) = SplitCommandData(callbackQuery.Data);
        var commandHandlers = new Dictionary<string, Func<long, string, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            {"change_language", HandleChangeLanguageCommandAsync },
            {"set_language", HandleSetLanguageCommandAsync }
        };
        var chatId = callbackQuery.Message.Chat.Id;
        if (commandHandlers.TryGetValue(commandKey, out var handler))
        {
            await handler(chatId, data, cancellationToken);
        }
        else
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Unknown command.",
                cancellationToken: cancellationToken);
        }
    }

    public async Task HandleChangeLanguageCommandAsync(long chatId, string messageText, CancellationToken cancellationToken)
    {
        var languageKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("English", "set_language:EN") },
            new[] { InlineKeyboardButton.WithCallbackData("Українська", "set_language:UA") }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: "🌐 Select your language / Оберіть мову:",
            replyMarkup: languageKeyboard,
            cancellationToken: cancellationToken);
    }

    public async Task HandleSetLanguageCommandAsync(long chatId, string messageText, CancellationToken cancellationToken)
    {
        await _userStateService.SetLanguageAsync(chatId, messageText, cancellationToken);
        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(messageText, "LanguageSet", messageText),
            cancellationToken: cancellationToken);

        await _mainMenuHandler.ShowMainMenuAsync(chatId, cancellationToken);
    }
}
