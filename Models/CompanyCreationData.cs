namespace Telegram.Bot.Models;

public enum CompanyFlowMode
{
    Create,
    Edit
}

public class CompanyCreationData
{
    public CompanyFlowMode Mode { get; set; } = CompanyFlowMode.Create;
    public required string CompanyName { get; set; }
    public string? CompanyLocation { get; set; }
    public int EmployeeCount { get; set; }
    public List<EmployeeCreationData> Employees { get; set; } = new();
    public int CurrentEmployeeIndex { get; set; }
    public required string CompanyAlias { get; set; }
    public List<ServiceCreationData> Services { get; set; } = new();
    public int CurrentServiceIndex { get; set; }
    public EditingContext EditingContext { get; set; } = EditingContext.None;
    public int? SelectedEmployeeId { get; set; }  // Make nullable to differentiate between modes
    public List<string> WorkingDays { get; set; } = new();
    public TimeSpan DefaultStartTime { get; set; }
    public TimeSpan DefaultEndTime { get; set; }
}

public enum EditingContext
{
    None,

    // Company fields
    CompanyName,
    CompanyDescription,

    // Service fields
    ServiceName,
    ServicePrice,
    ServiceDuration,
    ServiceDescription,
    ServiceSelection, // selecting which service to edit

    // Employee fields
    EmployeeName,
    EmployeeSelection,
    AssignServiceToEmployee
}
