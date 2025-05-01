using System.ComponentModel.DataAnnotations;
using Telegram.Bot.Enums;

namespace Telegram.Bot.Models;
public class UserState
{
    [Key]
    public long ChatId { get; set; }
    public UserRole Role { get; set; } = UserRole.Unknown;
    public UserRole ActiveRole { get; set; } = UserRole.Unknown; 
    public string? Language { get; set; } = "EN";
}