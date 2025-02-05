using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Examples.WebHook.Models;

namespace Telegram.Bot.Examples.WebHook.Services
{
     public interface ITokensService
    {
        Task<Token> GetTokenByValue(string tokenValue);
        Task<Token> GetTokenByChatId(long chatId);
        Task AssociateChatIdWithToken(long chatId, string tokenValue);
    }
    public class TokensService : ITokensService
    {
        private readonly BookingDbContext _dbContext;

        public TokensService(BookingDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task CreateCompanyWithToken(string companyName, int numberOfEmployees)
        {
            var company = new Company
            {
                Name = companyName,
            };

            _dbContext.Companies.Add(company);
            await _dbContext.SaveChangesAsync();

            // Generate a company token
            var companyToken = new Token
            {
                TokenValue = Guid.NewGuid().ToString(),
                Type = TokenType.Company,
                CompanyId = company.Id,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Tokens.Add(companyToken);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<string> GenerateClientToken(int companyId, int clientId)
        {
            var clientToken = new Token
            {
                TokenValue = Guid.NewGuid().ToString(),
                Type = TokenType.Client,
                CompanyId = companyId,
                ClientId = clientId,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Tokens.Add(clientToken);
            await _dbContext.SaveChangesAsync();

            return clientToken.TokenValue; // Return the token value to be used by the client
        }

        public async Task<Token> GetTokenByValue(string tokenValue)
        {
            return await _dbContext.Tokens
                .Include(t => t.Company)
                .FirstOrDefaultAsync(t => t.TokenValue == tokenValue);
        }

        public async Task<Token> GetTokenByChatId(long chatId)
        {
            return await _dbContext.Tokens
                .Include(t => t.Company)
                .FirstOrDefaultAsync(t => t.ChatId == chatId);
        }

        public async Task AssociateChatIdWithToken(long chatId, string tokenValue)
        {
            var token = await _dbContext.Tokens
                .FirstOrDefaultAsync(t => t.TokenValue == tokenValue);

            if (token != null)
            {
                token.ChatId = chatId;
                token.Used = true;
                await _dbContext.SaveChangesAsync();
            }
        }
    }
}

