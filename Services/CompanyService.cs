using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Examples.WebHook;
using Telegram.Bot.Examples.WebHook.Models;

namespace Telegram.Bot.Services;

public class CompanyService : ICompanyService
{
    private readonly BookingDbContext _dbContext;

    public CompanyService(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
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
} 