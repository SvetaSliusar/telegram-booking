using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Services;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Models;
using System.Text;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands;

public class BreakCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ILogger<BreakCommandHandler> _logger;

    public BreakCommandHandler(
        IUserStateService userStateService, 
        BookingDbContext dbContext,
        ITelegramBotClient botClient,
        ILogger<BreakCommandHandler> logger)
    {
        _userStateService = userStateService;
        _dbContext = dbContext;
        _botClient = botClient;
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
            { "add_break", HandleAddBreakAsync },
            { "select_break_start", HandleSelectBreakStartAsync },
            { "select_break_end", HandleSelectBreakEndAsync },
            { "remove_break", HandleRemoveBreakAsync },
            { "remove_break_confirmation", HandleRemoveBreakConfirmationAsync },
            { "back_to_breaks", HandleBackToBreaksAsync },
            { "manage_breaks", HandleManageBreaksAsync },
            { "select_day_for_breaks", HandleDaySelectionForBreaksAsync }
        };

        if (commandHandlers.TryGetValue(commandKey, out var handler))
        {
            await handler(chatId, data, cancellationToken);
        }
        else
        {
           _logger.LogError("Unknown break command: {CommandKey}", commandKey);
        }
    }

    private async Task HandleAddBreakAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var (employerId, day) = ParseEmployerIdAndDayFromData(data);
        var language = _userStateService.GetLanguage(chatId);
        _userStateService.SetConversation(chatId, $"WaitingForBreakStart_{employerId}_{(int)day}");

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BreakStartTime"),
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), $"select_day_for_breaks:{employerId}_{(int)day}") }
            }),
            cancellationToken: cancellationToken);
    }

    private async Task HandleRemoveBreakAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var (employerId, day) = ParseEmployerIdAndDayFromData(data);
        var language = _userStateService.GetLanguage(chatId);
        
        // Get working hours for this day
        var workingHours = await _dbContext.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == day && 
                                     wh.Employee.Company.Token.ChatId == chatId, 
                              cancellationToken);

        if (workingHours == null || !workingHours.Breaks.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoBreaks"),
                cancellationToken: cancellationToken);
            return;
        }

        // Create keyboard with break options
        var keyboard = new List<InlineKeyboardButton[]>();
        foreach (var breakTime in workingHours.Breaks.OrderBy(b => b.StartTime))
        {
            keyboard.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{breakTime.StartTime:hh\\:mm} - {breakTime.EndTime:hh\\:mm}",
                    $"remove_break_confirmation:{day}_{breakTime.Id}")
            });
        }

        keyboard.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                Translations.GetMessage(language, "Back"),
                $"back_to_breaks:{day}")
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SelectBreakToRemove"),
            replyMarkup: new InlineKeyboardMarkup(keyboard),
            cancellationToken: cancellationToken);
    }

    private async Task HandleDaySelectionForBreaksAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var (employerId, day) = ParseEmployerIdAndDayFromData(data);
        var language = _userStateService.GetLanguage(chatId);
        var employee = await _dbContext.Employees
            .Include(e => e.WorkingHours)
                .ThenInclude(wh => wh.Breaks)
        .FirstOrDefaultAsync(e => e.Id == employerId, cancellationToken);

        if (employee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoEmployeeFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var workingHours = employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == day);
        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoWorkingHoursForDay"),
                cancellationToken: cancellationToken);
            return;
        }

        // Show current breaks and options
        var messageBuilder = new StringBuilder();
        messageBuilder.AppendLine(Translations.GetMessage(language, "CurrentBreaks"));
        
        if (workingHours.Breaks.Any())
        {
            foreach (var breakTime in workingHours.Breaks.OrderBy(b => b.StartTime))
            {
                var breakText = string.Format(Translations.GetMessage(language, "BreakFormat",
                    breakTime.StartTime.ToString(@"hh\:mm"),
                    breakTime.EndTime.ToString(@"hh\:mm")));
                messageBuilder.AppendLine(breakText);
            }
        }
        else
        {
            messageBuilder.AppendLine(Translations.GetMessage(language, "NoBreaks"));
        }

        var buttons = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    Translations.GetMessage(language, "AddBreak"),
                    $"add_break:{employerId}_{(int)day}")
            }
        };

        if (workingHours.Breaks.Any())
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    Translations.GetMessage(language, "RemoveBreak"),
                    $"remove_break:{employerId}_{(int)day}")
            });
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), "manage_breaks") });

        await _botClient.SendMessage(
            chatId: chatId,
            text: messageBuilder.ToString(),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    private async Task HandleSelectBreakStartAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var (day, startTime) = ParseDayAndTimeFromData(data);

        var language = _userStateService.GetLanguage(chatId);
        
        var workingHours = await _dbContext.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == day && 
                                     wh.Employee.Company.Token.ChatId == chatId, 
                              cancellationToken);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoWorkingHoursSet"),
                cancellationToken: cancellationToken);
            return;
        }

        var keyboard = new List<InlineKeyboardButton[]>();
        var currentTime = startTime.Add(TimeSpan.FromMinutes(30));

        while (currentTime <= workingHours.EndTime)
        {
            var row = new List<InlineKeyboardButton>();
            for (int i = 0; i < 4 && currentTime <= workingHours.EndTime; i++)
            {
                row.Add(InlineKeyboardButton.WithCallbackData(
                    currentTime.ToString(@"hh\:mm"),
                    $"select_break_end:{day}:{startTime}:{currentTime}"));
                currentTime = currentTime.Add(TimeSpan.FromMinutes(30));
            }
            keyboard.Add(row.ToArray());
        }

        keyboard.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(
                Translations.GetMessage(language, "Back"),
                $"back_to_breaks:{day}")
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SelectBreakEndTime"),
            replyMarkup: new InlineKeyboardMarkup(keyboard),
            cancellationToken: cancellationToken);
    }

    private async Task HandleSelectBreakEndAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var (day, startTime, endTime) = ParseDayStartEndTimeFromData(data);

        var language = _userStateService.GetLanguage(chatId);
        
        // Get working hours for this day
        var workingHours = await _dbContext.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == day && 
                                     wh.Employee.Company.Token.ChatId == chatId, 
                              cancellationToken);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoWorkingHoursSet"),
                cancellationToken: cancellationToken);
            return;
        }

        // Check if the break overlaps with any existing breaks
        if (workingHours.Breaks.Any(b => 
            (startTime >= b.StartTime && startTime < b.EndTime) ||
            (endTime > b.StartTime && endTime <= b.EndTime) ||
            (startTime <= b.StartTime && endTime >= b.EndTime)))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "BreakOverlap"),
                cancellationToken: cancellationToken);
            return;
        }

        // Add the break
        workingHours.Breaks.Add(new Break
        {
            StartTime = startTime,
            EndTime = endTime
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BreakAdded", 
                startTime.ToString(@"hh\:mm"), 
                endTime.ToString(@"hh\:mm")),
            cancellationToken: cancellationToken);

        await HandleWorkingHoursSelection(chatId, day, workingHours.StartTime, workingHours.EndTime, cancellationToken);
    }

    private async Task HandleRemoveBreakConfirmationAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var (day, breakId) = ParseDayAndIdFromData(data);

        var language = _userStateService.GetLanguage(chatId);
        
        // Get working hours for this day
        var workingHours = await _dbContext.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == day && 
                                     wh.Employee.Company.Token.ChatId == chatId, 
                              cancellationToken);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoWorkingHoursSet"),
                cancellationToken: cancellationToken);
            return;
        }

        var breakToRemove = workingHours.Breaks.FirstOrDefault(b => b.Id == breakId);
        if (breakToRemove == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "BreakNotFound"),
                cancellationToken: cancellationToken);
            return;
        }

        workingHours.Breaks.Remove(breakToRemove);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BreakRemoved"),
            cancellationToken: cancellationToken);

        // Show updated breaks list
        await HandleWorkingHoursSelection(chatId, day, workingHours.StartTime, workingHours.EndTime, cancellationToken);
    }

    private async Task HandleBackToBreaksAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var day = Enum.Parse<DayOfWeek>(data);

        var workingHours = await _dbContext.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.DayOfWeek == day &&
                                       wh.Employee.Company.Token.ChatId == chatId,
                                 cancellationToken);

        if (workingHours != null)
        {
            await HandleWorkingHoursSelection(chatId, day, workingHours.StartTime, workingHours.EndTime, cancellationToken);
        }
    }

    private async Task HandleManageBreaksAsync(long chatId, string data, CancellationToken cancellationToken)
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
                        $"select_day_for_breaks:{employee.Id}_{(int)day}")
                });
            }
        }

        dayButtons.Add(new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), "back_to_menu") });

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SelectDay"),
            replyMarkup: new InlineKeyboardMarkup(dayButtons),
            cancellationToken: cancellationToken);
    }

    private async Task HandleWorkingHoursSelection(long chatId, DayOfWeek day, TimeSpan startTime, TimeSpan endTime, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        
        // Get the company for this chat ID
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
                .ThenInclude(e => e.WorkingHours)
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

        // Get or create working hours for this day
        var workingHours = employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == day);
        if (workingHours == null)
        {
            workingHours = new WorkingHours
            {
                DayOfWeek = day,
                EmployeeId = employee.Id
            };
            employee.WorkingHours.Add(workingHours);
        }

        workingHours.StartTime = startTime;
        workingHours.EndTime = endTime;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Show current breaks and options to add/remove breaks
        var messageBuilder = new StringBuilder();
        messageBuilder.AppendLine(Translations.GetMessage(language, "WorkingHoursSet", 
            startTime.ToString(@"hh\:mm"), 
            endTime.ToString(@"hh\:mm")));
        
        messageBuilder.AppendLine();
        messageBuilder.AppendLine(Translations.GetMessage(language, "CurrentBreaks"));

        if (workingHours.Breaks.Any())
        {
            foreach (var breakTime in workingHours.Breaks.OrderBy(b => b.StartTime))
            {
                messageBuilder.AppendLine(string.Format(Translations.GetMessage(language, "BreakFormat"),
                    breakTime.StartTime.ToString(@"hh\:mm"),
                    breakTime.EndTime.ToString(@"hh\:mm")));
            }
        }
        else
        {
            messageBuilder.AppendLine(Translations.GetMessage(language, "NoBreaks"));
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    Translations.GetMessage(language, "AddBreak"),
                    $"add_break:{day}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    Translations.GetMessage(language, "RemoveBreak"),
                    $"remove_break:{day}")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    Translations.GetMessage(language, "Back"),
                    "back_to_days")
            }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: messageBuilder.ToString(),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}
