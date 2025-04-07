namespace Telegram.Bot.Models;

public class EmployeeCreationData
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<int> Services { get; set; }
    public List<DayOfWeek> WorkingDays { get; set; }
    public List<WorkingHoursData> WorkingHours { get; set; }
}