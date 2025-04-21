namespace Telegram.Bot.Models;

public class EmployeeCreationData
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public List<int> Services { get; set; } = new();
    public List<DayOfWeek> WorkingDays { get; set; } = new();
    public List<WorkingHoursData> WorkingHours { get; set; } = new();
}