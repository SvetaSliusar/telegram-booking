namespace Telegram.Bot.Models;

public class ServiceCreationData
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public int Duration { get; set; }
    public required string Description { get; set; }
    public required Currency Currency { get; set; }
}

public enum Currency
{
    USD,
    EUR,
    UAH
}
