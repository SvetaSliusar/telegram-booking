using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Examples.WebHook;

namespace Telegram.Bot.Services;

public interface IStartCommandHandler
{
    Task<bool> HandleStartCommandAsync(string messageText, long chatId, CancellationToken cancellationToken);
}

public class StartCommandHandler : IStartCommandHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly CompanyUpdateHandler _companyUpdateHandler;
    private readonly ClientUpdateHandler _clientUpdateHandler;
    private readonly ICompanyService _companyService;
    private readonly ILogger<StartCommandHandler> _logger;

    public StartCommandHandler(
        ITelegramBotClient botClient,
        BookingDbContext dbContext,
        CompanyUpdateHandler companyUpdateHandler,
        ClientUpdateHandler clientUpdateHandler,
        ICompanyService companyService,
        ILogger<StartCommandHandler> logger)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _companyUpdateHandler = companyUpdateHandler;
        _clientUpdateHandler = clientUpdateHandler;
        _companyService = companyService;
        _logger = logger;
    }

    public async Task<bool> HandleStartCommandAsync(string messageText, long chatId, CancellationToken cancellationToken)
    {
        // Check if the message starts with /start
        if (!messageText.StartsWith("/start"))
        {
            return true;
        }

        // Extract parameter - handle both /start=123 and /start 123 formats
        string parameter;
        if (messageText.StartsWith("/start="))
        {
            parameter = messageText.Substring(7); // Remove "/start=" prefix
        }
        else
        {
            parameter = messageText.Substring(6).Trim(); // Remove "/start" prefix and trim spaces
        }

        if (string.IsNullOrEmpty(parameter))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Please provide a valid company token or company alias.",
                cancellationToken: cancellationToken);
            return false;
        }
        
        // First try to find a token (can be any combination of characters and numbers)
        var token = await _dbContext.Tokens
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.TokenValue == parameter, cancellationToken);

        if (token != null && !token.Used)
        {
            token.ChatId = chatId;
            token.Used = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _companyUpdateHandler.ShowMainMenu(chatId, cancellationToken);
            return true;
        }

        // If no valid token found, check if parameter is a company alias (client access)
        var company = await _companyService.GetCompanyByAliasAsync(parameter, cancellationToken);

        if (company != null)
        {
            await _clientUpdateHandler.StartClientFlow(chatId, company.Id, cancellationToken);
            return false;
        }

        // If neither token nor company alias is valid
        await _botClient.SendMessage(
            chatId: chatId,
            text: "❌ Invalid parameter. Please use a valid company token or company alias.",
            cancellationToken: cancellationToken);
        return false;
    }
} 