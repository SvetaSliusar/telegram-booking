using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

// <summary>
//     /// Interface for managing the state of company creation.
//     /// </summary>
//     /// <remarks>
//         This interface provides methods to manage the state of company creation, including setting company name,
//         description, adding/removing services and employees, and assigning/unassigning services to employees.
//     /// </remarks>
public interface ICompanyCreationStateService
{
    CompanyCreationData GetState(long chatId);
    void SetCompanyName(long chatId, string name);
    int AddService(long chatId, ServiceCreationData service);
    void UpdateService(long chatId, ServiceCreationData updatedService);
    void RemoveService(long chatId, int serviceId);

    int AddEmployee(long chatId, EmployeeCreationData employee);
    void UpdateEmployee(long chatId, EmployeeCreationData updatedEmployee);
    void RemoveEmployee(long chatId, int employeeId);

    void AssignServiceToEmployee(long chatId, int employeeId, int serviceId);

    void ClearState(long chatId);
}

