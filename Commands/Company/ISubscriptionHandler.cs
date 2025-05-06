
namespace Telegram.Bot.Commands.Company;

public interface ISubscriptionHandler
{
    Task ShowSubscriptionsAsync(long chatId, CancellationToken cancellationToken);
    Task<string?> CreateStripeSessionAsync(long chatId, string data, CancellationToken cancellationToken);
}