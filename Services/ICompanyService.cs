using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public interface ICompanyService
{
    Task<Company?> GetFirstCompanyAsync(CancellationToken cancellationToken);
    Task<Company?> GetCompanyByAliasAsync(string alias, CancellationToken cancellationToken);
    Task<List<Service>> GetCompanyServicesAsync(int companyId, CancellationToken cancellationToken);
} 