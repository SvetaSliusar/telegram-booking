using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Commands.Helpers;
using Telegram.Bot.Enums;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Company;

public class WorkTimeCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly ICompanyCreationStateService _companyCreationStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ILogger<WorkTimeCommandHandler> _logger;
    private readonly ITranslationService _translationService;

    public WorkTimeCommandHandler(
        IUserStateService userStateService, 
        BookingDbContext dbContext,
        ITelegramBotClient botClient,
        ICompanyCreationStateService companyCreationStateService,
        ILogger<WorkTimeCommandHandler> logger,
        ITranslationService translationService)
    {
        _userStateService = userStateService;
        _dbContext = dbContext;
        _botClient = botClient;
        _companyCreationStateService = companyCreationStateService;
        _logger = logger;
        _translationService = translationService;
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
            { "clear_working_hours", HandleClearWorkingHoursAsync },
            { "set_company_timezone", HandleSetTimezoneAsync }
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

    private async Task HandleSetTimezoneAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var state = _companyCreationStateService.GetState(chatId);
        var employeeId = state.CurrentEmployeeIndex;
        var timezone = Enum.Parse<SupportedTimezone>(data);
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        _companyCreationStateService.SetTimezone(chatId, employeeId, timezone);

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "TimezoneSet", timezone.ToTimezoneId()),
            cancellationToken: cancellationToken);
        
        _userStateService.SetConversation(chatId, $"WaitingForDefaultStartTime_{timezone}");

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "EnterDefaultStartTime"),
            cancellationToken: cancellationToken);
    }
    
    private async Task HandleSelectDayForWorkTimeAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var (employerId, day) = ParseEmployerIdAndDayFromData(data);
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

         _userStateService.SetConversation(chatId, $"WaitingForWorkStartTime_{employerId}_{(int)day}");

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "WorkStartTime"),
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "Back"), "change_work_time") }
            }),
        cancellationToken: cancellationToken);
    }

    private async Task HandleChangeWorkTimeAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
                .ThenInclude(e => e.WorkingHours)
                .ThenInclude(wh => wh.Breaks)
        .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                parseMode: ParseMode.MarkdownV2,
                text: _translationService.Get(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var employee = company.Employees.FirstOrDefault();
        if (employee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                parseMode: ParseMode.MarkdownV2,
                text: _translationService.Get(language, "NoEmployeeFound"),
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
                var dayName = _translationService.Get(language, day.ToString());
                dayButtons.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{dayName} ({workingHours.StartTime.ToString(@"hh\:mm")} - {workingHours.EndTime.ToString(@"hh\:mm")}) {workingHours.Timezone}",
                        $"select_day_for_work_time_start:{employee.Id}_{(int)day}")
                });
            }
        }

        dayButtons.Add(new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "Back"), "back_to_menu") });

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "SelectDayForWorkTime"),
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
        var state = _companyCreationStateService.GetState(chatId);
        if (state.Employees == null || state.Employees.Count == 0)
        {
            await _botClient.SendMessage(chatId, "Select working days first.", cancellationToken: cancellationToken);
            return;
        }

        await ShowTimezoneSelection(chatId, cancellationToken);
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

    private async Task ShowTimezoneSelection(long chatId, CancellationToken cancellationToken)
    {
        var timezoneButtons = Enum.GetValues(typeof(SupportedTimezone))
            .Cast<SupportedTimezone>()
            .Select(tz => new[] {
                InlineKeyboardButton.WithCallbackData(tz.ToString().Replace('_', '/'), $"set_company_timezone:{tz}")
            })
            .ToArray();

        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var keyboard = new InlineKeyboardMarkup(timezoneButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "SelectTimezone"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}