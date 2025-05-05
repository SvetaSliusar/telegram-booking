using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Commands.Common;
using Telegram.Bot.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Client;

public class ShareContactCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly IUserStateService _userStateService;
    private readonly ITranslationService _translationService;
    private readonly IMainMenuCommandHandler _mainMenuCommandHandler;

    public ShareContactCommandHandler(
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
        
        await HandleUsernameRequestAsync(callbackQuery.Message, cancellationToken);
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
}
