namespace Telegram.Bot.Examples.WebHook.Models;

public class Client
{
    public long Id { get; set; }
    public string Name { get; set; }
    public long ChatId { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; }
    public ICollection<Booking> Bookings { get; set; }
    public int TokenId { get; set; }
    public Token Token { get; set; }
}