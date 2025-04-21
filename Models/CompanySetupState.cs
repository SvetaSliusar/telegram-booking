namespace Telegram.Bot.Models
{
    public class CompanySetupState
    {
        public SetupStep CurrentStep { get; set; }
        public required Company Company { get; set; }
        public List<DayOfWeek> SelectedWorkDays { get; set; } = new();
        public Dictionary<DayOfWeek, (TimeSpan Start, TimeSpan End)> WorkingHours { get; set; } = new();
        public TimeSpan DefaultInterval { get; set; } = TimeSpan.FromMinutes(30);
        public required string Language { get; set; }
    }

    public enum SetupStep
    {
        Initial,
        EnteringName,
        UploadingPhoto,
        SelectingWorkDays,
        SelectingWorkHours,
        SettingInterval,
        AddingServices,
        Complete
    } 
}