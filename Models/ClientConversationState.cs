namespace Telegram.Bot.Models;

public class ClientConversationState
{
    public const string EnglishLanguage = "English";
    public const string UkrainianLanguage = "Українська";

    public int CompanyId { get; set; }
    public string Language { get; set; }
    public int SelectedServiceId { get; set; }
    public DateTime SelectedMonth { get; set; }
    public DateTime SelectedDay { get; set; }
    public ConversationStep CurrentStep { get; set; } = ConversationStep.AwaitingLanguage;
}

public enum ConversationStep
{
    AwaitingLanguage,
    SelectingService,
    SelectingMonth,
    SelectingDay,
    SelectingTimeSlot
} 