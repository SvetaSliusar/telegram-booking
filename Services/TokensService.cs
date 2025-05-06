using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services
{
     public interface ITokensService
    {
        Task<Token?> GetTokenByValue(string tokenValue);
        Task<Token?> GetTokenByChatId(long chatId);
        Task AssociateChatIdWithToken(long chatId, string tokenValue);
        Task<bool> AddCompanySetupTokenAsync(long chatId, string language, string customerId);
        Task<long?> GetChatIdByCustomerIdAsync(string customerId, CancellationToken cancellationToken);
    }
    
    public class TokensService : ITokensService
    {
        private readonly BookingDbContext _dbContext;

        public TokensService(BookingDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Token?> GetTokenByValue(string tokenValue)
        {
            return await _dbContext.Tokens
                .Include(t => t.Company)
                .SingleOrDefaultAsync(t => t.TokenValue == tokenValue);
        }

        public async Task<Token?> GetTokenByChatId(long chatId)
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

        public async Task<bool> AddCompanySetupTokenAsync(long chatId, string language, string customerId)
        {            
            var existingToken = await _dbContext.Tokens
                .FirstOrDefaultAsync(t => t.ChatId == chatId);

            if (existingToken != null)
            {
                existingToken.StripeCustomerId = customerId;
                _dbContext.Tokens.Update(existingToken);
            }
            else
            {
                var newToken = new Token
                {
                    TokenValue = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow,
                    Used = false,
                    ChatId = chatId,
                    Language = language,
                    StripeCustomerId = customerId
                };

                await _dbContext.Tokens.AddAsync(newToken);
            }

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<long?> GetChatIdByCustomerIdAsync(string customerId, CancellationToken cancellationToken)
        {
            var token = await _dbContext.Tokens
                .FirstOrDefaultAsync(t => t.StripeCustomerId == customerId, cancellationToken: cancellationToken);

            return token?.ChatId;
        }
    }
}

