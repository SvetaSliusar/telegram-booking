using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands;
public class ChangeLanguageCommandHandler : ICallbackCommand
{
    private readonly IUserStateService _userStateService;
    private readonly ITelegramBotClient _botClient;

    public ChangeLanguageCommandHandler(
        IUserStateService userStateService,
        ITelegramBotClient botClient)
    {
        _userStateService = userStateService;
        _botClient = botClient;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;

        var (commandKey, data) = SplitCommandData(callbackQuery.Data);
        var commandHandlers = new Dictionary<string, Func<long, string, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            {"change_language", HanldeChangeLanguageAsync },
            {"set_language", HandleSetLanguageAsync }
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

    public async Task HanldeChangeLanguageAsync(long chatId, string data, CancellationToken cancellationToken)
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

    public async Task HandleSetLanguageAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        _userStateService.SetLanguage(chatId, data);
        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(data, "LanguageSet", data),
            cancellationToken: cancellationToken);
    }
}
