using System.Collections.Concurrent;
using Telegram.Bot.Enums;
using Telegram.Bot.Models;

namespace Telegram.Bot.Services;

public class CompanyCreationStateService : ICompanyCreationStateService
{
    private readonly ConcurrentDictionary<long, CompanyCreationData> _state = new();
    private int _nextServiceId = 1;

    public CompanyCreationData GetState(long chatId)
    {
        return _state.GetOrAdd(chatId, _ => new CompanyCreationData
        {
            CompanyName = string.Empty,
            CompanyAlias = string.Empty,
            CompanyLocation = string.Empty,
            Mode = CompanyFlowMode.Create,
            Services = new List<ServiceCreationData>(),
            Employees = new List<EmployeeCreationData>(),
            WorkingDays = new List<string>(),
            DefaultStartTime = new TimeSpan(0, 0, 0),
            DefaultEndTime = new TimeSpan(0, 0, 0)
        });
    }

    public void SetCompanyName(long chatId, string name)
    {
        UpdateState(chatId, state => state.CompanyName = name);
    }

    public void SetCompanyAlias(long chatId, string alias)
    {
        UpdateState(chatId, state => state.CompanyAlias = alias);
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
                service.Currency = updatedService.Currency;
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
                    CompanyName = string.Empty,
                    CompanyAlias = string.Empty,
                    CompanyLocation = string.Empty,
                    Services = new List<ServiceCreationData>(),
                    Employees = new List<EmployeeCreationData>()
                };
                updateAction(newState);
                return newState;
            },
            (_, existingState) =>
            {
                if (existingState.Employees == null)
                {
                    existingState.Employees = new List<EmployeeCreationData>();
                    existingState.Services = new List<ServiceCreationData>();
                }
                updateAction(existingState);
                return existingState;
            });
    }

    public void AddWorkingDayToEmployee(long chatId, int employeeId, DayOfWeek day)
    {
        UpdateState(chatId, state =>
        {
            var employee = state.Employees.FirstOrDefault(e => e.Id == employeeId);
            if (employee != null && !employee.WorkingDays.Contains(day))
            {
                employee.WorkingDays.Add(day);
            }
        });
    }

    public void ClearWorkingHours(long chatId, int employeeId)
    {
        UpdateState(chatId, state =>
        {
            var employee = state.Employees.FirstOrDefault(e => e.Id == employeeId);
            if (employee != null)
            {
                employee.WorkingHours.Clear();
            }
        });
    }
    
    public void ClearWorkingDays(long chatId, int employeeId)
    {
        UpdateState(chatId, state =>
        {
            var employee = state.Employees.FirstOrDefault(e => e.Id == employeeId);
            if (employee != null)
            {
                employee.WorkingDays.Clear();
            }
        });
    }

    public void AddDefaultStartTimeToEmployee(long chatId, int employeeId, TimeSpan startTime)
    {
        UpdateState(chatId, state =>
        {
            var employee = state.Employees.FirstOrDefault(e => e.Id == employeeId);
            if (employee != null && employee.WorkingDays.Count > 0)
            {
                foreach (var workday in employee.WorkingDays)
                {
                    var workingHours = employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == workday);
                    if (workingHours != null)
                    {
                        workingHours.StartTime = startTime;
                    }
                    else
                    {
                        employee.WorkingHours.Add(new WorkingHoursData
                        {
                            DayOfWeek = workday,
                            StartTime = startTime,
                            EndTime = TimeSpan.Zero
                        });
                    }
                }
            }
        });
    }

    public void AddDefaultEndTimeToEmployee(long chatId, int employeeId, TimeSpan endTime)
    {
        UpdateState(chatId, state =>
        {
            var employee = state.Employees.FirstOrDefault(e => e.Id == employeeId);
            if (employee != null && employee.WorkingDays.Count > 0)
            {
                foreach (var workday in employee.WorkingDays)
                {
                    var workingHours = employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == workday);
                    if (workingHours != null)
                    {
                        workingHours.EndTime = endTime;
                    }
                    else
                    {
                        employee.WorkingHours.Add(new WorkingHoursData
                        {
                            DayOfWeek = workday,
                            StartTime = TimeSpan.Zero,
                            EndTime = endTime
                        });
                    }
                }
            }
        });
    }

    public void SetTimezone(long chatId, int employeeId, SupportedTimezone timezone)
    {
        UpdateState(chatId, state =>
        {
            var employee = state.Employees.FirstOrDefault(e => e.Id == employeeId);
            if (employee != null && employee.WorkingDays.Count > 0)
            {
                foreach (var workday in employee.WorkingDays)
                {
                    var workingHours = employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == workday);
                    if (workingHours != null)
                    {
                        workingHours.Timezone = timezone;
                    }
                }
            }
        });
    }
}
