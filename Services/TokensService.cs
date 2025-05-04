using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services
{
     public interface ITokensService
    {
        Task<Token?> GetTokenByValue(string tokenValue);
        Task<Token?> GetTokenByChatId(long chatId);
        Task AssociateChatIdWithToken(long chatId, string tokenValue);
        Task AddCompanySetupTokenAsync(long chatId);
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

        public async Task AddCompanySetupTokenAsync(long chatId)
        {
            var token = Guid.NewGuid().ToString();
            var newToken = new Token
            {
                TokenValue = token,
                CreatedAt = DateTime.UtcNow,
                Used = false,
                ChatId = chatId
            };

            await _dbContext.Tokens.AddAsync(newToken);
            await _dbContext.SaveChangesAsync();
        }
    }
}

