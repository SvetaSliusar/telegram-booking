using Telegram.Bot.Enums;

namespace Telegram.Bot.Models;

public class WorkingHoursData
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public SupportedTimezone Timezone { get; set; }

    public int EmployeeId { get; set; } 
    
}