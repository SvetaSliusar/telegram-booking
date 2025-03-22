using System;
namespace Telegram.Bot.Examples.WebHook.Models
{
    public class Company
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Alias { get; set; } // URL-friendly version of the name without spaces
        public virtual List<Employee> Employees { get; set; }
        public int TokenId { get; set; }
        public Token Token { get; set; }
    }

    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int CompanyId { get; set; }
        public virtual Company Company { get; set; }
        public virtual List<Service> Services { get; set; }
        public virtual List<WorkingHours> WorkingHours { get; set; }
    }
}
