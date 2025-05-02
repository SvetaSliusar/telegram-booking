using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Client;

public class RequestCompanyCreationCommandHanlder : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly IUserStateService _userStateService;
    private readonly ITranslationService _translationService;

    public RequestCompanyCreationCommandHanlder(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext,
        ITranslationService translationService)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _userStateService = userStateService;
        _translationService = translationService;
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
        var language = await _userStateService.GetLanguageAsync(message.Chat.Id, cancellationToken);
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
                text: _translationService.Get(language, "NewContactThanks"),
                parseMode: ParseMode.MarkdownV2,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
            _userStateService.RemoveConversation(message.Chat.Id);
        }
        else
        {
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _translationService.Get(language, "NoContactAccess"),
                parseMode: ParseMode.MarkdownV2,
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleUsernameRequestAsync(Message message, CancellationToken cancellationToken)
    {
        var username = message.Chat?.Username;
        var language = await _userStateService.GetLanguageAsync(message.Chat.Id, cancellationToken);
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
                text: _translationService.Get(language, "ContactRequestSent", username),
                parseMode: ParseMode.MarkdownV2,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
             _userStateService.RemoveConversation(message.Chat.Id);
        }
        else
        {
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _translationService.Get(language, "NoUsername"),
                parseMode: ParseMode.MarkdownV2,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
        }
    }

    private async Task HanldeManualContactRequestAsync(Message message, CancellationToken cancellationToken)
    {
        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: _translationService.Get(await _userStateService.GetLanguageAsync(message.Chat.Id, cancellationToken), "ManualContact"),
            parseMode: Types.Enums.ParseMode.Markdown,
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: cancellationToken);
        
         _userStateService.SetConversation(message.Chat.Id, "WaitingForContactInfo");
    }

    private async Task HandleRequestContactAsync(Message message, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(message.Chat.Id, cancellationToken);
        _userStateService.SetConversation(message.Chat.Id, "WaitingForContactInfo");
        // Reply Keyboard for phone sharing (RequestContact = true)
        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton(_translationService.Get(language, "SharePhone")) { RequestContact = true } }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        // Inline keyboard for username or manual contact
        var inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "UseTelegramUsername"), "share_username_request") },
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "TypeContact"), "manual_contact_request") }
        });

        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: _translationService.Get(language, "SharePhonePrompt"),
            parseMode: ParseMode.MarkdownV2,
            replyMarkup: replyKeyboard,
            cancellationToken: cancellationToken);

        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: _translationService.Get(language, "ContactOptions"),
            parseMode: ParseMode.MarkdownV2,
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken);
    }
}
