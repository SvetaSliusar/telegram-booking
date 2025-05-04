using System.ComponentModel.DataAnnotations;

namespace Telegram.Bot.Models;

public class Token
{
    [Key]
    public int Id { get; set; }
    public required string TokenValue { get; set; }
    public bool Used { get; set; }
    public long? ChatId { get; set; }
    public string? Language { get; set; }
    public virtual Company Company { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
} 