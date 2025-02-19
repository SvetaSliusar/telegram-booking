namespace Telegram.Bot.Examples.WebHook.Models;

public class Token
{
    public int Id { get; set; }
    public string TokenValue { get; set; }
    public TokenType Type { get; set; }
    public long? ChatId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Used { get; set; }
    public int? CompanyId { get; set; }
    public virtual Company Company { get; set; }
} 