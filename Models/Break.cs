namespace Telegram.Bot.Examples.WebHook.Models
{
    public class Break
    {
        public int Id { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int WorkingHoursId { get; set; }
        public virtual WorkingHours WorkingHours { get; set; }
    }
} 