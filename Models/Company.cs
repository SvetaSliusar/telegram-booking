using Telegram.Bot.Enums;
namespace Telegram.Bot.Models
{
    public class Company
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public string Location { get; set; } = string.Empty;
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Trial;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public required string Alias { get; set; }
        public virtual List<Employee> Employees { get; set; } = new();
        public int TokenId { get; set; }
        public required Token Token { get; set; }
        public ReminderSettings? ReminderSettings { get; set; }
        public virtual List<ClientCompanyInvite> ClientInvites { get; set; } = new();
        public virtual ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
    }

    public class Employee
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public int CompanyId { get; set; }
        public required virtual Company Company { get; set; }
        public virtual List<Service> Services { get; set; } = new();
        public virtual List<WorkingHours> WorkingHours { get; set; } = new();
    }
}
