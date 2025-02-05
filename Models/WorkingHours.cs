namespace Telegram.Bot.Examples.WebHook.Models
{
    public class WorkingHours
    {
        public int Id { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int EmployeeId { get; set; }
        public virtual Employee Employee { get; set; }
    } 
}