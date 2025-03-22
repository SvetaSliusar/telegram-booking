using Telegram.Bot.Examples.WebHook.Models;

namespace Telegram.Bot.Services;

public interface ICompanyService
{
    Task<Company?> GetFirstCompanyAsync(CancellationToken cancellationToken);
    Task<Company?> GetCompanyByAliasAsync(string alias, CancellationToken cancellationToken);
    Task<List<Service>> GetCompanyServicesAsync(int companyId, CancellationToken cancellationToken);
} 