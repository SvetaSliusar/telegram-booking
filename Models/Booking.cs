using System;
namespace Telegram.Bot.Models
{
    public class Booking
    {
        public int Id { get; set; }

        // The time of the booking
        public DateTime BookingTime { get; set; }

        // Foreign key to the Service
        public int ServiceId { get; set; }
        public Service Service { get; set; }

        // Foreign key to the Company
        public int CompanyId { get; set; }
        public Company Company { get; set; }

        // Foreign key to Client
        public long ClientId { get; set; }
        public Client Client { get; set; }

        // Reminder tracking
        public bool ReminderSent { get; set; }
    }

}

