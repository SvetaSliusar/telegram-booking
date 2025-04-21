using System.Formats.Asn1;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands;

public class WorkTimeCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly ICompanyCreationStateService _companyCreationStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ILogger<WorkTimeCommandHandler> _logger;

    public WorkTimeCommandHandler(
        IUserStateService userStateService, 
        BookingDbContext dbContext,
        ITelegramBotClient botClient,
        ICompanyCreationStateService companyCreationStateService,
        ILogger<WorkTimeCommandHandler> logger)
    {
        _userStateService = userStateService;
        _dbContext = dbContext;
        _botClient = botClient;
        _companyCreationStateService = companyCreationStateService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;

        var chatId = callbackQuery.Message.Chat.Id;
        var (commandKey, data) = SplitCommandData(callbackQuery.Data);

        var commandHandlers = new Dictionary<string, Func<long, string, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            { "init_work_time", HandleInitWorkTimeAsync },
            { "select_day_for_work_time_start", HandleSelectDayForWorkTimeAsync },
            { "setup_work_time_start", HandleSetupWorkTimeStartAsync },
            { "setup_work_time_end", HandleSetupWorkTimeEndAsync },
            { "change_work_time", HandleChangeWorkTimeAsync },
            { "confirm_working_hours", HandleConfirmWorkingHoursAsync },
            { "clear_working_hours", HandleClearWorkingHoursAsync }
        };

        if (commandHandlers.TryGetValue(commandKey, out var commandHandler))
        {
            await commandHandler(chatId, data, cancellationToken);
        }
        else
        {
            await _botClient.SendMessage(chatId, "Unknown command.", cancellationToken: cancellationToken);
        }
    }

    private static readonly string[,] morningWorkingHours = {{ "8:00", "8:30", "9:00"} , {"9:30", "10:00", "11:00" }};
    private static readonly string[,] afternoonWorkingHours = {{ "12:00", "12:30", "13:00"}, {"13:30", "14:00", "14:30" }};
    private static readonly string[,] eveningWorkingHours = {{ "19:00", "19:30", "20:00"}, { "20:30", "21:00", "22:00" }};
    
    private async Task HandleSelectDayForWorkTimeAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var (employerId, day) = ParseEmployerIdAndDayFromData(data);
        var language = _userStateService.GetLanguage(chatId);

         _userStateService.SetConversation(chatId, $"WaitingForWorkStartTime_{employerId}_{(int)day}");

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "WorkStartTime"),
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), "change_work_time") }
            }),
            cancellationToken: cancellationToken);
    }

    private async Task HandleChangeWorkTimeAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
                .ThenInclude(e => e.WorkingHours)
                .ThenInclude(wh => wh.Breaks)
        .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var employee = company.Employees.FirstOrDefault();
        if (employee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoEmployeeFound"),
                cancellationToken: cancellationToken);
            return;
        }

        // Show day selection
        var dayButtons = new List<InlineKeyboardButton[]>();
        foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
        {
            var workingHours = employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == day);
            if (workingHours != null)
            {
                var dayName = Translations.GetMessage(language, day.ToString());
                dayButtons.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{dayName} ({workingHours.StartTime.ToString(@"hh\:mm")} - {workingHours.EndTime.ToString(@"hh\:mm")})",
                        $"select_day_for_work_time_start:{employee.Id}_{(int)day}")
                });
            }
        }

        dayButtons.Add(new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), "back_to_menu") });

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SelectDayForWorkTime"),
            replyMarkup: new InlineKeyboardMarkup(dayButtons),
            cancellationToken: cancellationToken);
    }

    private async Task HandleSetupWorkTimeEndAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var endTime = TimeSpan.Parse(data, CultureInfo.InvariantCulture);
        var state = _companyCreationStateService.GetState(chatId);

        var employeeId = state.CurrentEmployeeIndex;
        var workingHours = state.Employees.FirstOrDefault(x => x.Id == employeeId)?.WorkingHours.FirstOrDefault();

        if (workingHours?.StartTime <= workingHours?.EndTime)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "Start time must be before end time.",
                cancellationToken: cancellationToken);
            return;
        }

        _companyCreationStateService.AddDefaultEndTimeToEmployee(
            chatId, employeeId, endTime);

        await HandleInitWorkTimeAsync(chatId, "start", cancellationToken);
    }

    private async Task HandleSetupWorkTimeStartAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var state = _companyCreationStateService.GetState(chatId);

        var employeeId = state.CurrentEmployeeIndex;
        _companyCreationStateService.AddDefaultStartTimeToEmployee(
            chatId, employeeId, TimeSpan.Parse(data, CultureInfo.InvariantCulture));

        await HandleInitWorkTimeAsync(chatId, "end", cancellationToken);
    }

    private async Task HandleInitWorkTimeAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);

        var state = _companyCreationStateService.GetState(chatId);
        if (state.Employees == null || state.Employees.Count == 0)
        {
            await _botClient.SendMessage(chatId, "Select working days first.", cancellationToken: cancellationToken);
            return;
        }

        _userStateService.SetConversation(chatId, $"WaitingForDefaultStartTime");

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "EnterDefaultStartTime"),
            cancellationToken: cancellationToken);
    }

    private async Task HandleConfirmWorkingHoursAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var state = _companyCreationStateService.GetState(chatId);

        var employeeId = state.CurrentEmployeeIndex;
        var workingHours = state.Employees.FirstOrDefault(x => x.Id == employeeId)?.WorkingHours;

        if (workingHours != null && workingHours.Count > 0)
        {
            var employee = await _dbContext.Employees.FindAsync(employeeId);
            if (employee == null)
            {
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "❌ Employee not found.",
                    cancellationToken: cancellationToken);
                return;
            }

            _dbContext.WorkingHours.AddRange(workingHours.Select(wh => new WorkingHours
            {
                EmployeeId = employeeId,
                DayOfWeek = wh.DayOfWeek,
                StartTime = wh.StartTime,
                EndTime = wh.EndTime,
                Employee = employee
            }));
            await _dbContext.SaveChangesAsync();
            _companyCreationStateService.ClearWorkingDays(chatId, employeeId);
            _companyCreationStateService.ClearWorkingHours(chatId, employeeId);

            await _botClient.SendMessage(
                chatId: chatId,
                text: "✅ Working hours confirmed.",
                cancellationToken: cancellationToken);
        }
        else
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ No working hours selected. Please select working hours first.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleClearWorkingHoursAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var state = _companyCreationStateService.GetState(chatId);

        var employeeId = state.CurrentEmployeeIndex;
        _companyCreationStateService.ClearWorkingHours(chatId, employeeId);

        var existingHours = _dbContext.WorkingHours
            .Where(w => w.EmployeeId == state.CurrentEmployeeIndex);
        if (await existingHours.AnyAsync())
        {
            _dbContext.WorkingHours.RemoveRange(existingHours);
            await _dbContext.SaveChangesAsync();
        }

        await HandleInitWorkTimeAsync(chatId, "start", cancellationToken);
    }
}