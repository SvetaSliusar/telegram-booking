using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.Bot.Commands;

public class GenerateClientLinkHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;

    public GenerateClientLinkHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _dbContext = dbContext;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
         if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;

        await GenerateClientLink(chatId, cancellationToken);
    }

    private async Task GenerateClientLink(long chatId, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: Company not found.",
                cancellationToken: cancellationToken);
            return;
        }

        var botUsername = (await _botClient.GetMe(cancellationToken)).Username;
        var clientLink = $"https://t.me/{botUsername}?start={company.Alias}";

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "ClientLink", clientLink),
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }
}
