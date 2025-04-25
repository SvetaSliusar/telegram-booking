namespace Telegram.Bot.Models;

public class Service
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public required string Description { get; set; }
    public required Currency Currency { get; set; }
    public TimeSpan Duration { get; set; }  // Duration of the service (e.g., 1 hour)
    public int EmployeeId { get; set; }
    public required virtual Employee Employee { get; set; }
}

public class TimeSlot
{
    public required string Start { get; set; }
    public required string End { get; set; }
}
