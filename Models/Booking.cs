using System;
namespace Telegram.Bot.Models
{
    public enum BookingStatus
    {
        Pending,
        Confirmed,
        Rejected,
        Cancelled
    }

    public class Booking
    {
        public int Id { get; set; }

        // The time of the booking
        public DateTime BookingTime { get; set; }

        // Foreign key to the Service
        public int ServiceId { get; set; }
        public required Service Service { get; set; }

        // Foreign key to the Company
        public int CompanyId { get; set; }
        public required Company Company { get; set; }

        // Foreign key to Client
        public long ClientId { get; set; }
        public required Client Client { get; set; }

        // Reminder tracking
        public bool ReminderSent { get; set; }

        // Booking status
        public BookingStatus Status { get; set; } = BookingStatus.Pending;
    }

}

