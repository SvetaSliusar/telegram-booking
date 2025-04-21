using System;

namespace Telegram.Bot.Models
{
    public class ReminderSettings
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public required Company Company { get; set; }
        public int HoursBeforeReminder { get; set; } = 24; // Default to 24 hours
    }
} 