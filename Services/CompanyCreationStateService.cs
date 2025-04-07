using System.Collections.Concurrent;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public class CompanyCreationStateService : ICompanyCreationStateService
{
    private readonly ConcurrentDictionary<long, CompanyCreationData> _state = new();
    private int _nextServiceId = 1;
    private int _nextEmployeeId = 1;

    public CompanyCreationData GetState(long chatId)
    {
        return _state.GetOrAdd(chatId, _ => new CompanyCreationData());
    }

    public void SetCompanyName(long chatId, string name)
    {
        UpdateState(chatId, state => state.CompanyName = name);
    }

    public int AddService(long chatId, ServiceCreationData service)
    {
        service.Id = Interlocked.Increment(ref _nextServiceId);
        UpdateState(chatId, state => state.Services.Add(service));
        return service.Id;
    }

    public void UpdateService(long chatId, ServiceCreationData updatedService)
    {
        UpdateState(chatId, state =>
        {
            var service = state.Services.FirstOrDefault(s => s.Id == updatedService.Id);
            if (service != null)
            {
                service.Name = updatedService.Name;
                service.Price = updatedService.Price;
                service.Duration = updatedService.Duration;
            }
        });
    }

    public void RemoveService(long chatId, int serviceId)
    {
        UpdateState(chatId, state =>
        {
            state.Services.RemoveAll(s => s.Id == serviceId);
            foreach (var employee in state.Employees)
            {
                employee.Services.Remove(serviceId);
            }
        });
    }

    public int AddEmployee(long chatId, EmployeeCreationData employee)
    {
        employee.Id = Interlocked.Increment(ref _nextEmployeeId);
        UpdateState(chatId, state => state.Employees.Add(employee));
        return employee.Id;
    }

    public void UpdateEmployee(long chatId, EmployeeCreationData updatedEmployee)
    {
        UpdateState(chatId, state =>
        {
            var employee = state.Employees.FirstOrDefault(e => e.Id == updatedEmployee.Id);
            if (employee != null)
            {
                employee.Name = updatedEmployee.Name;
                employee.Services = updatedEmployee.Services;
            }
        });
    }

    public void RemoveEmployee(long chatId, int employeeId)
    {
        UpdateState(chatId, state =>
        {
            state.Employees.RemoveAll(e => e.Id == employeeId);
        });
    }

    public void AssignServiceToEmployee(long chatId, int employeeId, int serviceId)
    {
        UpdateState(chatId, state =>
        {
            var employee = state.Employees.FirstOrDefault(e => e.Id == employeeId);
            if (employee != null && !employee.Services.Contains(serviceId))
            {
                employee.Services.Add(serviceId);
            }
        });
    }

    public void ClearState(long chatId)
    {
        _state.TryRemove(chatId, out _);
    }

    private void UpdateState(long chatId, Action<CompanyCreationData> updateAction)
    {
        _state.AddOrUpdate(chatId,
            _ =>
            {
                var newState = new CompanyCreationData
                {
                    Services = new List<ServiceCreationData>(),
                    Employees = new List<EmployeeCreationData>()
                };
                updateAction(newState);
                return newState;
            },
            (_, existingState) =>
            {
                updateAction(existingState);
                return existingState;
            });
    }
}
