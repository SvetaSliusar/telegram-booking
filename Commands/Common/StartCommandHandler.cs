using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Commands.Common;

public interface IStartCommandHandler
{
    Task<bool> HandleStartCommandAsync(string messageText, long chatId, CancellationToken cancellationToken);
}

public class StartCommandHandler : IStartCommandHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly ICompanyService _companyService;
    private readonly ILogger<StartCommandHandler> _logger;
    private readonly IMainMenuCommandHandler _mainMenuCommandHandler;
    private const string DefaultLanguage = "EN";
    private const string StartCommand = "/start";
    private const string StartCommandWithParameter = "/start=";
    private const int DemoCompanyId = 1; 

    public StartCommandHandler(
        ITelegramBotClient botClient,
        BookingDbContext dbContext,
        ICompanyService companyService,
        ILogger<StartCommandHandler> logger,
        IMainMenuCommandHandler mainMenuCommandHandler)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _companyService = companyService;
        _logger = logger;
        _mainMenuCommandHandler = mainMenuCommandHandler;
    }

    public async Task<bool> HandleStartCommandAsync(string messageText, long chatId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageText) || !messageText.StartsWith("/start"))
            return false;

        string parameter = ExtractStartParameter(messageText);

        if (string.IsNullOrEmpty(parameter))
            return await HandleStartWithoutParameter(chatId, cancellationToken);

        return await HandleStartWithParameter(parameter, chatId, cancellationToken);
    }

    private static string ExtractStartParameter(string messageText)
    {
        if (messageText.StartsWith(StartCommandWithParameter))
            return messageText.Substring(StartCommandWithParameter.Length);

        return messageText.Substring(StartCommand.Length).Trim();
    }

    private async Task<bool> HandleStartWithoutParameter(long chatId, CancellationToken cancellationToken)
    {
        var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
        if (client != null)
        {
            await _mainMenuCommandHandler.ShowClientMainMenuAsync(chatId, client.Language ?? DefaultLanguage, cancellationToken);
            return true;
        }

        var company = await _dbContext.Companies
            .Include(c => c.Token)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company != null)
        {
            await _mainMenuCommandHandler.ShowCompanyMainMenuAsync(chatId, company.Token.Language ?? DefaultLanguage, cancellationToken);
            return true;
        }

        await AddClientIfNotExists(chatId, DemoCompanyId, cancellationToken);
        await ShowInitialLanguageSelection(chatId, cancellationToken);

        return true;
    }

    private async Task<bool> HandleStartWithParameter(string parameter, long chatId, CancellationToken cancellationToken)
    {
        var token = await _dbContext.Tokens
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.TokenValue == parameter, cancellationToken);

        if (token is { Used: true, ChatId: var usedChatId } && usedChatId == chatId)
            return false;

        if (token != null && !token.Used)
        {
            token.ChatId = chatId;
            token.Used = true;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await ShowInitialLanguageSelection(chatId, cancellationToken);
            return true;
        }

        var company = await _companyService.GetCompanyByAliasAsync(parameter, cancellationToken);
        if (company != null)
        {
            await AddClientIfNotExists(chatId, company.Id, cancellationToken);
            await ShowInitialLanguageSelection(chatId, cancellationToken);
            return true;
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: "❌ Invalid parameter. Please use a valid company token or alias.",
            cancellationToken: cancellationToken);

        return true;
    }

    private async Task AddClientIfNotExists(long chatId, int companyId, CancellationToken cancellationToken)
    {
        var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
        if (client == null)
        {
            client = new Models.Client
            {
                ChatId = chatId,
                Name = "New Client",
                TimeZoneId = "UTC"
            };
            _dbContext.Clients.Add(client);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var inviteExists = await _dbContext.ClientCompanyInvites
            .AnyAsync(i => i.ClientId == client.Id && i.CompanyId == companyId, cancellationToken);

        if (!inviteExists)
        {
            var invite = new ClientCompanyInvite
            {
                ClientId = client.Id,
                CompanyId = companyId,
                InviteDate = DateTime.UtcNow,
                Used = false
            };
            _dbContext.ClientCompanyInvites.Add(invite);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ShowInitialLanguageSelection(long chatId, CancellationToken cancellationToken)
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
}
