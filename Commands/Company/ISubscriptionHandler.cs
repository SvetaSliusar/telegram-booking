
namespace Telegram.Bot.Commands.Company;

public interface ISubscriptionHandler
{
    Task ShowSubscriptionsAsync(long chatId, CancellationToken cancellationToken);
}