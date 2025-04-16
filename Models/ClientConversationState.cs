using Telegram.Bot.Services;

namespace Telegram.Bot.Models;

public class ClientConversationState
{
    public object Lock { get; } = new object();

    public int CompanyId { get; set; }

    public string CurrentConversation { get; set; }

    public CompanyCreationData CompanyCreationData { get; set; } = new CompanyCreationData();

    public ServiceCreationData ServiceCreationData { get; set; } = new ServiceCreationData();

    public ConversationStep CurrentStep { get; set; } = ConversationStep.None;
    
}

public enum ConversationStep
{
    None,
    CompanyCreation,
    ServiceCreation,
    AwaitingLanguage,
    SelectingService,
    SelectingMonth,
    SelectingDay,
    SelectingTimeSlot
} 