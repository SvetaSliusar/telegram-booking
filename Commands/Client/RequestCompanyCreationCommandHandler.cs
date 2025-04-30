using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Client;

public class RequestCompanyCreationCommandHanlder : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly IUserStateService _userStateService;

    public RequestCompanyCreationCommandHanlder(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _userStateService = userStateService;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;
        var (commandKey, _) = SplitCommandData(callbackQuery.Data);

        var commandHandlers = new Dictionary<string, Func<Message, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            { "share_phone_request", HandlePhoneRequestAsync },
            { "share_username_request", HandleUsernameRequestAsync },
            { "request_company_creation", HandleRequestContactAsync },
            { "manual_contact_request", HanldeManualContactRequestAsync }
        };

        if (commandHandlers.TryGetValue(commandKey, out var commandHandler))
        {
            await commandHandler(callbackQuery?.Message, cancellationToken);
        }
        else
        {
            await _botClient.SendMessage(chatId, "Unknown command.", cancellationToken: cancellationToken);
        }
    }

    private async Task HandlePhoneRequestAsync(Message message, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(message.Chat.Id);
        if (message.Contact != null)
        {
            var phoneNumber = message.Contact.PhoneNumber;

            var adminChatId = await _dbContext.Companies
                .Where(c => c.Alias == "demo")
                .Select(c => c.Token.ChatId)
                .FirstOrDefaultAsync(cancellationToken);
            await _botClient.SendMessage(
                chatId: adminChatId,
                text: $"📩 New company creation request!\n\nFrom chat ID: {message.Chat.Id}\nPhone number: {phoneNumber}",
                cancellationToken: cancellationToken);
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: Translations.GetMessage(language, "NewContactThanks"),
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
            _userStateService.RemoveConversation(message.Chat.Id);
        }
        else
        {
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: Translations.GetMessage(language, "NoContactAccess"),
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleUsernameRequestAsync(Message message, CancellationToken cancellationToken)
    {
        var username = message.Chat?.Username;
        var language = _userStateService.GetLanguage(message.Chat.Id);
        if (!string.IsNullOrEmpty(username))
        {
            var adminChatId = await _dbContext.Companies
                .Where(c => c.Alias == "demo")
                .Select(c => c.Token.ChatId)
                .FirstOrDefaultAsync(cancellationToken);

            await _botClient.SendMessage(
                chatId: adminChatId,
                text: $"📨 New company request:\nUsername: @{username} (ChatId: {message.Chat.Id})",
                cancellationToken: cancellationToken);

            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: Translations.GetMessage(language, "ContactRequestSent", username),
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
             _userStateService.RemoveConversation(message.Chat.Id);
        }
        else
        {
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: Translations.GetMessage(language, "NoUsername"),
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
        }
    }

    private async Task HanldeManualContactRequestAsync(Message message, CancellationToken cancellationToken)
    {
        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: Translations.GetMessage(_userStateService.GetLanguage(message.Chat.Id), "ManualContact"),
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: cancellationToken);
        
         _userStateService.SetConversation(message.Chat.Id, "WaitingForContactInfo");
    }

    private async Task HandleRequestContactAsync(Message message, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(message.Chat.Id);
        _userStateService.SetConversation(message.Chat.Id, "WaitingForContactInfo");
        // Reply Keyboard for phone sharing (RequestContact = true)
        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton(Translations.GetMessage(language, "SharePhone")) { RequestContact = true } }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        // Inline keyboard for username or manual contact
        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "UseTelegramUsername"), "share_username_request") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "TypeContact"), "manual_contact_request") }
        });

        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: Translations.GetMessage(language, "SharePhonePrompt"),
            replyMarkup: replyKeyboard,
            cancellationToken: cancellationToken);

        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: Translations.GetMessage(language, "ContactOptions"),
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken);
    }
}
