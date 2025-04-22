using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services;

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

    public StartCommandHandler(
        ITelegramBotClient botClient,
        BookingDbContext dbContext,
        ICompanyService companyService,
        ILogger<StartCommandHandler> logger)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _companyService = companyService;
        _logger = logger;
    }

    public async Task<bool> HandleStartCommandAsync(string messageText, long chatId, CancellationToken cancellationToken)
    {
        if (!messageText.StartsWith("/start"))
        {
            return false;
        }

        string parameter;
        if (messageText.StartsWith("/start="))
        {
            parameter = messageText.Substring(7);
        }
        else
        {
            parameter = messageText.Substring(6).Trim();
        }

        if (string.IsNullOrEmpty(parameter))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Please provide a valid company token or company alias.",
                cancellationToken: cancellationToken);
            return true;
        }

        var token = await _dbContext.Tokens
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.TokenValue == parameter, cancellationToken);
        if (token != null && string.IsNullOrEmpty(token.Language) && !token.Used)
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
            var client = await _dbContext.Clients
                .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

            if (client == null)
            {
                client = new Client
                {
                    ChatId = chatId,
                    Name = "New Client",
                    TimeZoneId = "UTC"
                };
                _dbContext.Clients.Add(client);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var existingInvite = await _dbContext.ClientCompanyInvites
                .FirstOrDefaultAsync(cci => cci.ClientId == client.Id && cci.CompanyId == company.Id, cancellationToken);

            if (existingInvite == null)
            {
                var invite = new ClientCompanyInvite
                {
                    ClientId = client.Id,
                    CompanyId = company.Id,
                    InviteDate = DateTime.UtcNow,
                    Used = false,
                    Client = client,
                    Company = company
                };
                _dbContext.ClientCompanyInvites.Add(invite);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await ShowInitialLanguageSelection(chatId, cancellationToken);
            return true;
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: "❌ Invalid parameter. Please use a valid company token or company alias.",
            cancellationToken: cancellationToken);
        return true;
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