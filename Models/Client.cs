namespace Telegram.Bot.Models;

public class Client
{
    public long Id { get; set; }
    public string Name { get; set; }
    public long ChatId { get; set; }
    public ICollection<Booking> Bookings { get; set; }
    public string Language { get; set; } = "EN";
    public string TimeZoneId { get; set; } = "UTC"; // Default to UTC if not specified
}