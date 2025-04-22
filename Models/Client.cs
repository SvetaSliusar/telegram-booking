using System.ComponentModel.DataAnnotations;

namespace Telegram.Bot.Models;

public class Client
{
    public long Id { get; set; }

    [Required]
    public long ChatId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string TimeZoneId { get; set; } = "UTC";

    public string? Language { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastActiveAt { get; set; }

    public virtual ICollection<ClientCompanyInvite> CompanyInvites { get; set; } = new List<ClientCompanyInvite>();
}