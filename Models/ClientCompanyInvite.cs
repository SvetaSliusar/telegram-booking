using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Telegram.Bot.Models
{
    public class ClientCompanyInvite
    {
        public int Id { get; set; }
        
        // The client who was invited
        [Required]
        public long ClientId { get; set; }

        [ForeignKey(nameof(ClientId))]
        public virtual Client Client { get; set; } = null!;
        
        // The company that invited the client
        [Required]
        public int CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        public virtual Company Company { get; set; } = null!;
        
        // When the invite was created
        [Required]
        public DateTime InviteDate { get; set; } = DateTime.UtcNow;
        
        // Whether the client has used this invite to make a booking
        [Required]
        public bool Used { get; set; } = false;
    }
} 