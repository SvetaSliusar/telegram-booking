using System;
namespace Telegram.Bot.Examples.WebHook.Models
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

        // Foreign key to Client (optional)
        public long ClientId { get; set; } // Telegram ID of the client
    }

}

