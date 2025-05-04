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

public class RequestContactHandler : ICallbackCommand, IShareContactHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly IUserStateService _userStateService;
    private readonly ITranslationService _translationService;
    private readonly IMainMenuCommandHandler _mainMenuCommandHandler;

    public RequestContactHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext,
        ITranslationService translationService,
        IMainMenuCommandHandler mainMenuCommandHandler)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _userStateService = userStateService;
        _translationService = translationService;
        _mainMenuCommandHandler = mainMenuCommandHandler;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;
        var (commandKey, _) = SplitCommandData(callbackQuery.Data);

        var commandHandlers = new Dictionary<string, Func<Message, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            { "share_contact_username", HandleUsernameRequestAsync }
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

    private async Task HandleUsernameRequestAsync(Message message, CancellationToken cancellationToken)
    {
        var username = message.Chat?.Username;
        var language = await _userStateService.GetLanguageAsync(message.Chat.Id, cancellationToken);
        if (!string.IsNullOrEmpty(username))
        {
            var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.ChatId == message.Chat.Id);
            if (client != null)
            {
                client.Username = username;
                _dbContext.Clients.Update(client);
                await _dbContext.SaveChangesAsync(cancellationToken);
                _userStateService.RemoveConversation(message.Chat.Id);
                await _mainMenuCommandHandler.ShowActiveMainMenuAsync(message.Chat.Id, cancellationToken);
            }
            else
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: _translationService.Get(language, "ClientNotFound"), 
                    replyMarkup: new ReplyKeyboardRemove(),
                    cancellationToken: cancellationToken);
            }
        }
        else
        {
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _translationService.Get(language, "NoUsername"), 
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
        }
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
