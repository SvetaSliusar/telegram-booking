using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Commands;
using Telegram.Bot.Commands.Common;

namespace Telegram.Bot.Services;
public class ClientUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly IUserStateService _userStateService;
    private readonly IMainMenuCommandHandler _mainMenuHandler;
    private readonly ICallbackCommandFactory _commandFactory;

    public ClientUpdateHandler(
        ITelegramBotClient botClient,
        BookingDbContext dbContext,
        IUserStateService userStateService,
        IMainMenuCommandHandler mainMenuHandler,
        ICallbackCommandFactory commandFactory)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _userStateService = userStateService;
        _mainMenuHandler = mainMenuHandler;
        _commandFactory = commandFactory;
    }

    public async Task StartClientFlow(long chatId, int companyId, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }

        // Show main menu
        await ShowMainMenu(chatId, cancellationToken);
    }

    private async Task ShowMainMenu(long chatId, CancellationToken cancellationToken)
    {
        await _mainMenuHandler.ShowMainMenuAsync(chatId, cancellationToken);
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        if (update == null) return;

        var handler = update switch
        {
            { Message: { } message } => BotOnMessageReceived(message, cancellationToken),
            { EditedMessage: { } message } => BotOnMessageReceived(message, cancellationToken),
            { CallbackQuery: { } callbackQuery } => BotOnCallbackQueryReceived(callbackQuery, cancellationToken),
            _ => Task.CompletedTask
        };

        await handler;
    }

    private async Task BotOnMessageReceived(Message message, CancellationToken cancellationToken)
    {
        if (message?.Text is not { } messageText) return;

        var chatId = message.Chat.Id;
        var language = _userStateService.GetLanguage(chatId);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "UseMenuButton"),
            cancellationToken: cancellationToken);
    }

    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrEmpty(callbackQuery.Data))
            return;

        var command = _commandFactory.CreateCommand(callbackQuery);
        if (command != null)
        {
            await command.ExecuteAsync(callbackQuery, cancellationToken);
            return;
        }
        var chatId = callbackQuery.Message.Chat.Id;
        var language = _userStateService.GetLanguage(chatId);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "UseMenuButton"),
            cancellationToken: cancellationToken);
    }
}
