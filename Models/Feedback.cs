using System;

namespace Telegram.Bot.Models
{
    public class Feedback
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public Company Company { get; set; }
        
        public string Message { get; set; }
        public DateTime CreatedAt { get; set; }

        public bool IsResolved { get; set; }
        public string? AdminResponse { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
} 