namespace Telegram.Bot.Examples.WebHook.Models;

public class Service
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string Description { get; set; }
    public TimeSpan Duration { get; set; }  // Duration of the service (e.g., 1 hour)
    public int EmployeeId { get; set; }
    public virtual Employee Employee { get; set; }
}

public class TimeSlot
{
    public string Start { get; set; }
    public string End { get; set; }
}
