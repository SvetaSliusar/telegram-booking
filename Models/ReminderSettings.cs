using System;

namespace Telegram.Bot.Examples.WebHook.Models
{
    public class ReminderSettings
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public Company Company { get; set; }
        public int HoursBeforeReminder { get; set; } = 24; // Default to 24 hours
    }
} 