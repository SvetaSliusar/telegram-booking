namespace Telegram.Bot.Models;

public class Client
{
    public long Id { get; set; }
    public string Name { get; set; }
    public long ChatId { get; set; }
    public ICollection<Booking> Bookings { get; set; }
    public string Language { get; set; } = "EN";
}