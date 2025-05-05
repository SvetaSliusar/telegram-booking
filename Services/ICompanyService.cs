using Telegram.Bot.Enums;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public interface ICompanyService
{
    Task<Company?> GetFirstCompanyAsync(CancellationToken cancellationToken);
    Task<Company?> GetCompanyByAliasAsync(string alias, CancellationToken cancellationToken);
    Task<List<Service>> GetCompanyServicesAsync(int companyId, CancellationToken cancellationToken);
    Task DisableCompanyAsync(long chatId, CancellationToken cancellationToken);
    Task EnableCompanyAsync(long chatId, CancellationToken cancellationToken);
    Task<PaymentStatus?> GetPaymentStatusAsync(long chatId, CancellationToken cancellationToken);
} 