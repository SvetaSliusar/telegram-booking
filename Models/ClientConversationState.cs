using Telegram.Bot.Services;

namespace Telegram.Bot.Models;

public class ClientConversationState
{
    public object Lock { get; } = new object();

    public int CompanyId { get; set; }

    public string CurrentConversation { get; set; } = string.Empty;

    public CompanyCreationData CompanyCreationData { get; set; } = new()
    {
        CompanyName = "New Company",
        CompanyAlias = "new-company",
        Mode = CompanyFlowMode.Create,
        Employees = new List<EmployeeCreationData>(),
        Services = new List<ServiceCreationData>(),
        WorkingDays = new List<string>(),
        DefaultStartTime = new TimeSpan(9, 0, 0), // 9:00 AM
        DefaultEndTime = new TimeSpan(17, 0, 0)  // 5:00 PM
    };

    public ServiceCreationData ServiceCreationData { get; set; } = new()
    {
        Name = "New Service",
        Description = "Service description",
        Price = 0,
        Duration = 30
    };

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