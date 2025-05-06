using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Telegram.Bot.Enums;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public class CompanyService : ICompanyService
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);
    private readonly BookingDbContext _dbContext;
    private readonly ILogger<CompanyService> _logger;

    public CompanyService(
        BookingDbContext dbContext,
        ILogger<CompanyService> logger,
        IMemoryCache cache)
    {
        _logger = logger;
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<Company?> GetFirstCompanyAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Companies
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Company?> GetCompanyByAliasAsync(string alias, CancellationToken cancellationToken)
    {
        return await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Alias.ToLower() == alias.ToLower(), cancellationToken);
    }

    public async Task<List<Service>> GetCompanyServicesAsync(int companyId, CancellationToken cancellationToken)
    {
        return await (from s in _dbContext.Services
                     join e in _dbContext.Employees on s.EmployeeId equals e.Id
                     where e.CompanyId == companyId
                     select s).ToListAsync(cancellationToken);
    }

    public async Task DisableCompanyAsync(long chatId, CancellationToken cancellationToken)
    {
        var token = await _dbContext.Tokens
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.ChatId == chatId, cancellationToken);

        var company = token?.Company;

        if (company == null)
        {
            _logger.LogWarning("Company not found for chat ID {ChatId} during disable.", chatId);
            return;
        }

        if (company.PaymentStatus != PaymentStatus.Failed)
        {
            company.PaymentStatus = PaymentStatus.Failed;
            _dbContext.Companies.Update(company);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _cache.Set($"company:status:{chatId}", PaymentStatus.Failed, _cacheDuration);
        }
    }

    public async Task EnableCompanyAsync(long chatId, CancellationToken cancellationToken)
    {
        var token = await _dbContext.Tokens
            .Include(t => t.Company)
            .FirstOrDefaultAsync(t => t.ChatId == chatId, cancellationToken);

        var company = token?.Company;

        if (company == null)
        {
            _logger.LogWarning("Company not found for chat ID {ChatId} during enable.", chatId);
            return;
        }

        if (company.PaymentStatus != PaymentStatus.Active)
        {
            company.PaymentStatus = PaymentStatus.Active;
            _dbContext.Companies.Update(company);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _cache.Set($"company:status:{chatId}", PaymentStatus.Active, _cacheDuration);
        }
    }

    public async Task<PaymentStatus?> GetPaymentStatusAsync(long chatId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue($"company:status:{chatId}", out PaymentStatus cachedStatus))
        {
            return cachedStatus;
        }

        var company = await _dbContext.Companies
            .Include(c => c.Token)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        var status = company?.PaymentStatus;

        _cache.Set($"company:status:{chatId}", status, _cacheDuration);

        return status;
    }

} 