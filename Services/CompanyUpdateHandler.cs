using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text;
using Telegram.Bot.Commands;
using System.Globalization;
using Telegram.Bot.Enums;

namespace Telegram.Bot.Services;

public class CompanyUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<CompanyUpdateHandler> _logger;
    private readonly BookingDbContext _dbContext;
    private readonly ICallbackCommandFactory _commandFactory;
    private readonly IUserStateService _userStateService;

    private readonly IEnumerable<IStateHandler> _stateHandlers;

    public CompanyUpdateHandler(
        ITelegramBotClient botClient,
        ILogger<CompanyUpdateHandler> logger,
        BookingDbContext dbContext,
        ICallbackCommandFactory callbackCommandFactory,
        IUserStateService userStateService,
        IEnumerable<IStateHandler> stateHandlers)
    {
        _botClient = botClient;
        _logger = logger;
        _dbContext = dbContext;
        _commandFactory = callbackCommandFactory;
        _userStateService = userStateService;
        _stateHandlers = stateHandlers;
    }

    public Mode GetMode(long chatId)
    {
        return _dbContext.Tokens.Any(t => t.ChatId == chatId) || 
            (_userStateService.GetConversation(chatId) == "WaitingForToken") ? Mode.Company : Mode.Client;
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        var handler = update switch
        {
            { Message: { } message } => BotOnMessageReceived(message, cancellationToken),
            { EditedMessage: { } message } => BotOnMessageReceived(message, cancellationToken),
            { CallbackQuery: { } callbackQuery } => BotOnCallbackQueryReceived(callbackQuery, cancellationToken),
            _ => Task.CompletedTask

        };

        await handler;
    }

    private async Task BotOnMessageReceived(Message message, CancellationToken cancellationToken)
    {       
        var messageText = string.IsNullOrEmpty(message.Text) 
            ? message.Venue?.Address 
            : message.Text;
        if (string.IsNullOrEmpty(messageText))
            return;

        var chatId = message.Chat.Id;
        var language = _userStateService.GetLanguage(chatId);
        var conversationState = _userStateService.GetConversation(chatId);

        var handler = _stateHandlers.FirstOrDefault(h => h.CanHandle(conversationState));
        if (handler != null)
        {
            await handler.HandleAsync(chatId, conversationState, messageText, cancellationToken);
            return;
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "UseMenuButton"),
            cancellationToken: cancellationToken);
    }

    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrEmpty(callbackQuery.Data))
            return;

        var command = _commandFactory.CreateCommand(callbackQuery);
        if (command != null)
        {
            await command.ExecuteAsync(callbackQuery, cancellationToken);
            return;
        }

        var chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data ?? string.Empty;

        try
        {
            if (data.StartsWith("reminder_time"))
            {
                await HandleReminderTimeSelection(callbackQuery, cancellationToken);
            return;
        }

            if (string.IsNullOrEmpty(data)) return;

        switch (data)
        {
            case "create_company":
                await StartCompanyCreation(chatId, cancellationToken);
                break;

            case "view_daily_bookings":
                _userStateService.SetConversation(chatId, "WaitingForBookingDate");
                await ShowBookingCalendar(chatId, DateTime.UtcNow, cancellationToken);
            break;

            case "reminder_settings":
                await HandleReminderSettings(chatId, cancellationToken);
            break;

            default:
                _logger.LogInformation("Unknown callback data: {CallbackData}", callbackQuery.Data);
                break;
        }
            if (data.StartsWith("select_day_for_breaks:"))
            {
                var parts = data.Split(':')[1].Split('_');
                var employeeId = int.Parse(parts[0]);
                var day = (DayOfWeek)int.Parse(parts[1]);
                await HandleDayBreaksSelection(chatId, employeeId, day, cancellationToken);
            return;
            }

            if (data.StartsWith("booking_date_"))
            {
                var selectedDate = DateTime.Parse(data.Replace("booking_date_", ""), CultureInfo.InvariantCulture);
                await HandleBookingDateSelection(chatId, selectedDate, cancellationToken);
            }
            else if (data.StartsWith("booking_prev_") || data.StartsWith("booking_next_"))
            {
                var referenceDate = DateTime.Parse(data.Replace("booking_prev_", "").Replace("booking_next_", ""), CultureInfo.InvariantCulture);
                DateTime newMonth = data.StartsWith("booking_prev_") 
                    ? referenceDate.AddMonths(-1) 
                    : referenceDate.AddMonths(1);
                await ShowBookingCalendar(chatId, newMonth, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing callback query: {CallbackData}", callbackQuery.Data);
            await _botClient.SendMessage(chatId, "An error occurred while processing the request. Please try again later.", cancellationToken: cancellationToken);
        }
    }

    private async Task StartCompanyCreation(long chatId, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);

        _userStateService.SetConversation(chatId, "WaitingForCompanyName");

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "EnterBusinessName"),
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private async Task ShowBookingCalendar(long chatId, DateTime selectedDate, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var daysInMonth = DateTime.DaysInMonth(selectedDate.Year, selectedDate.Month);
        var firstDayOfMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
        var currentDate = DateTime.UtcNow.Date;
        var nextMonth = currentDate.AddMonths(1);
        var isCurrentMonth = selectedDate.Year == currentDate.Year && selectedDate.Month == currentDate.Month;
        var isNextMonth = selectedDate.Year == nextMonth.Year && selectedDate.Month == nextMonth.Month;
        var isPastMonth = selectedDate < currentDate;
        var isFutureMonth = selectedDate > nextMonth;

        List<List<InlineKeyboardButton>> calendarButtons = new();

        // Weekday headers
        calendarButtons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("Mo", CallbackResponses.Ignore),
            InlineKeyboardButton.WithCallbackData("Tu", CallbackResponses.Ignore),
            InlineKeyboardButton.WithCallbackData("We", CallbackResponses.Ignore),
            InlineKeyboardButton.WithCallbackData("Th", CallbackResponses.Ignore),
            InlineKeyboardButton.WithCallbackData("Fr", CallbackResponses.Ignore),
            InlineKeyboardButton.WithCallbackData("Sa", CallbackResponses.Ignore),
            InlineKeyboardButton.WithCallbackData("Su", CallbackResponses.Ignore),
        });

        List<InlineKeyboardButton> weekRow = new();

        // Add empty buttons before the first day of the month
        for (int i = 1; i < (int)firstDayOfMonth.DayOfWeek; i++)
        {
            weekRow.Add(InlineKeyboardButton.WithCallbackData(" ", CallbackResponses.Ignore));
        }
        
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        // Get all bookings for this company in the current month
        var monthStart = DateTime.SpecifyKind(new DateTime(selectedDate.Year, selectedDate.Month, 1), DateTimeKind.Utc);
        var monthEnd = DateTime.SpecifyKind(monthStart.AddMonths(1).AddDays(-1), DateTimeKind.Utc);
        
        var bookedDates = _dbContext.Bookings != null
            ? await _dbContext.Bookings
                .Where(b => b.CompanyId == company.Id && 
                            b.BookingTime >= monthStart && 
                            b.BookingTime <= monthEnd)
                .Select(b => b.BookingTime.Date)
                .Distinct()
                .ToListAsync(cancellationToken)
            : new List<DateTime>();

        // Generate day buttons
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = DateTime.SpecifyKind(new DateTime(selectedDate.Year, selectedDate.Month, day), DateTimeKind.Utc);
            var hasBookings = bookedDates.Contains(date.Date);
            var isPastDate = date.Date < currentDate;
            
            if (isPastDate)
            {
                weekRow.Add(InlineKeyboardButton.WithCallbackData($"{day}⚫", CallbackResponses.Ignore));
            }
            else
            {
                var buttonText = hasBookings ? $"{day}📅" : day.ToString();
                weekRow.Add(InlineKeyboardButton.WithCallbackData(buttonText, $"booking_date_{date:yyyy-MM-dd}"));
            }

            if (weekRow.Count == 7) // New row after each week
            {
                calendarButtons.Add(weekRow);
                weekRow = new List<InlineKeyboardButton>();
            }
        }

        if (weekRow.Any()) // Add last row if not full
            calendarButtons.Add(weekRow);

        // Navigation buttons
        var prevButton = isPastMonth || isCurrentMonth
            ? InlineKeyboardButton.WithCallbackData("⬅️", CallbackResponses.Ignore)
            : InlineKeyboardButton.WithCallbackData("⬅️", $"booking_prev_{selectedDate:yyyy-MM-dd}");

        var nextButton = isFutureMonth || isNextMonth
            ? InlineKeyboardButton.WithCallbackData("➡️", CallbackResponses.Ignore)
            : InlineKeyboardButton.WithCallbackData("➡️", $"booking_next_{selectedDate:yyyy-MM-dd}");

        calendarButtons.Add(new List<InlineKeyboardButton>
        {
            prevButton,
            nextButton
        });

        var keyboard = new InlineKeyboardMarkup(calendarButtons);

        var messageText = Translations.GetMessage(language, "SelectDateForBookings") + "\n" +
                         $"{selectedDate:MMMM yyyy}\n" +
                         (isPastMonth ? "⚠️ Past month\n" : "") +
                         (isFutureMonth ? "⚠️ Future month\n" : "") +
                         "📅 Dates with bookings\n" +
                         "⚫ Past dates";

        await _botClient.SendMessage(
            chatId: chatId,
            text: messageText,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleBookingDateSelection(long chatId, DateTime selectedDate, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }
        var selectedDateUtc = DateTime.SpecifyKind(selectedDate, DateTimeKind.Utc);
        var dayStart = selectedDateUtc.Date;
        var dayEnd = dayStart.AddDays(1);

        var bookings = await _dbContext.Bookings
            .Include(b => b.Service)
                .ThenInclude(s => s.Employee)
            .Include(b => b.Client)
            .Where(b => b.CompanyId == company.Id && 
                        b.BookingTime >= dayStart && 
                        b.BookingTime < dayEnd)
            .OrderBy(b => b.BookingTime)
            .ToListAsync(cancellationToken);

        if (!bookings.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoBookingsForDate", selectedDate.ToString("dddd, MMMM d, yyyy")),
                replyMarkup: new InlineKeyboardMarkup(new[] 
                { 
                    new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), CallbackResponses.BackToMenu) }
                }),
                cancellationToken: cancellationToken);
            return;
        }

        var message = Translations.GetMessage(language, "BookingsForDate", selectedDate.ToString("dddd, MMMM d, yyyy")) + "\n\n";
        foreach (var booking in bookings)
        {
            var localTime = booking.BookingTime.ToLocalTime();
            message += Translations.GetMessage(language, "BookingDetailsForCompany", 
                booking.Service.Name,
                booking.Client.Name ?? "N/A",
                localTime.ToString("hh:mm tt"),
                booking.Client.Name ?? "N/A") + "\n\n";
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: message,
            replyMarkup: new InlineKeyboardMarkup(new[] 
            { 
                new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), CallbackResponses.BackToMenu) }
            }),
            cancellationToken: cancellationToken);
    }

    private async Task HandleReminderSettings(long chatId, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var company = await _dbContext.Companies
            .Include(c => c.ReminderSettings)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }

        // Create or get reminder settings
        if (company.ReminderSettings == null)
        {
            company.ReminderSettings = new ReminderSettings
            {
                CompanyId = company.Id,
                HoursBeforeReminder = 24,
                Company = company
            };
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "1hour"), "reminder_time:1") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "3hours"), "reminder_time:3") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "6hours"), "reminder_time:6") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "12hours"), "reminder_time:12") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "24hours"), "reminder_time:24") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), CallbackResponses.BackToMenu) }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SetReminderTime"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleReminderTimeSelection(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null) return;
        var chatId = callbackQuery.Message.Chat.Id;
        var language = _userStateService.GetLanguage(chatId);
        string data = callbackQuery.Data ?? string.Empty;
        if (string.IsNullOrEmpty(data))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidReminderTime"),
                cancellationToken: cancellationToken);
            return;
        }
        
        if (!data.StartsWith("reminder_time:")) return;

        var hoursStr = data.Split(':')[1];
        if (!int.TryParse(hoursStr, out int hours) || hours < 1 || hours > 24)
        {
            await _botClient.SendMessage(
            chatId: chatId,
                text: Translations.GetMessage(language, "InvalidReminderTime"),
            cancellationToken: cancellationToken);
            return;
        }

        var company = await _dbContext.Companies
            .Include(c => c.ReminderSettings)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }

        if (company.ReminderSettings == null)
        {
            company.ReminderSettings = new ReminderSettings
            {
                CompanyId = company.Id,
                HoursBeforeReminder = hours,
                Company = company
            };
        }
        else
        {
            company.ReminderSettings.HoursBeforeReminder = hours;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "ReminderTimeUpdated", hours),
            cancellationToken: cancellationToken);
        
        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "UseMenuButton"),
            cancellationToken: cancellationToken);
    }

    private async Task HandleDayBreaksSelection(long chatId, int employeeId, DayOfWeek day, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var employee = await _dbContext.Employees
            .Include(e => e.WorkingHours)
                .ThenInclude(wh => wh.Breaks)
        .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

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
                    $"add_break:{employeeId}_{(int)day}")
            }
        };

        if (workingHours.Breaks.Any())
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    Translations.GetMessage(language, "RemoveBreak"),
                    $"remove_break:{employeeId}_{(int)day}")
            });
        }

        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), "manage_breaks") });

        await _botClient.SendMessage(
            chatId: chatId,
            text: messageBuilder.ToString(),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

}
