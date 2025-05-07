
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Enums;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Client;

public class ChooseDateTimeCommandHandler : ICallbackCommand, ICalendarService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ITranslationService _translationService;

    public ChooseDateTimeCommandHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext,
        ITranslationService translationService)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _dbContext = dbContext;
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
            { "choose_date", HandleDateSelectionAsync },
            { "choose_this_month", ShowCurrentMonth },
            { "choose_prev_month", ShowPreviousMonth },
            { "choose_next_month", ShowNextMonth },
            { "choose_time", HandleTimeSelectionAsync },
            { "ignore", HandleIgnoreAsync}
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

    public Task ShowPreviousMonth(long chatId, string data, CancellationToken cancellationToken) =>
        ShowRelativeMonth(-1, chatId, data, cancellationToken);

    public Task ShowNextMonth(long chatId, string data, CancellationToken cancellationToken) =>
        ShowRelativeMonth(1, chatId, data, cancellationToken);

    public async Task ShowCurrentMonth(long chatId, string data, CancellationToken cancellationToken)
    {
        var currentDate = DateTime.UtcNow;

        await ShowCalendar(
            chatId: chatId,
            selectedDate: currentDate,
            cancellationToken: cancellationToken);
    }

    public async Task ShowRelativeMonth(int offsetInMonths, long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        
        if (!DateTime.TryParse(data, out var parsedDate))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "InvalidDaySelected"),
                cancellationToken: cancellationToken);
            return;
        }

        var targetMonth = parsedDate.AddMonths(offsetInMonths);

        await ShowCalendar(
            chatId: chatId,
            selectedDate: targetMonth,
            cancellationToken: cancellationToken);
    }

    public async Task ShowCalendar(long chatId, DateTime selectedDate, CancellationToken cancellationToken)
    {
        var currentDate = DateTime.UtcNow.Date;
        var currentMonthStart = new DateTime(currentDate.Year, currentDate.Month, 1);
        var nextMonthStart = currentMonthStart.AddMonths(1);
        var nextMonthEnd = nextMonthStart.AddMonths(1).AddTicks(-1);

        if (selectedDate < currentMonthStart || selectedDate > nextMonthEnd)
        {
            await _botClient.SendMessage(
                chatId,
                "⚠️ You can only book for this and next month.",
                cancellationToken: cancellationToken);
            return;
        }

        var state = _userStateService.GetConversation(chatId);
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

        if (!state.StartsWith("WaitingForDate_"))
        {
            await _botClient.SendMessage(
                chatId,
                _translationService.Get(language, "NoServiceSelected"),
                cancellationToken: cancellationToken);
            return;
        }

        var serviceId = int.Parse(state.Split('_')[1]);
        var service = await _dbContext.Services
            .Include(s => s.Employee)
                .ThenInclude(e => e.WorkingHours)
                    .ThenInclude(wh => wh.Breaks)
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service?.Employee == null)
        {
            await _botClient.SendMessage(
                chatId,
                _translationService.Get(language, "EmployeeNotFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var firstDayOfMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var daysInMonth = DateTime.DaysInMonth(firstDayOfMonth.Year, firstDayOfMonth.Month);
        var startIndex = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;

        var monthEnd = firstDayOfMonth.AddMonths(1).AddTicks(-1);

        var bookedTimes = await _dbContext.Bookings
            .Where(b => b.ServiceId == serviceId &&
                        b.BookingTime >= firstDayOfMonth &&
                        b.BookingTime <= monthEnd)
            .Select(b => b.BookingTime)
            .ToListAsync(cancellationToken);

        var calendarButtons = new List<List<InlineKeyboardButton>>
        {
            new() // Weekday headers
            {
                InlineKeyboardButton.WithCallbackData("Mo", "ignore"),
                InlineKeyboardButton.WithCallbackData("Tu", "ignore"),
                InlineKeyboardButton.WithCallbackData("We", "ignore"),
                InlineKeyboardButton.WithCallbackData("Th", "ignore"),
                InlineKeyboardButton.WithCallbackData("Fr", "ignore"),
                InlineKeyboardButton.WithCallbackData("Sa", "ignore"),
                InlineKeyboardButton.WithCallbackData("Su", "ignore"),
            }
        };

        var serviceDuration = service.Duration;
        var weekRow = new List<InlineKeyboardButton>();

        for (int i = 0; i < startIndex; i++)
            weekRow.Add(InlineKeyboardButton.WithCallbackData(" ", "ignore"));

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(selectedDate.Year, selectedDate.Month, day, 0, 0, 0, DateTimeKind.Utc);
            var isToday = date == currentDate;
            var workingHours = service.Employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == date.DayOfWeek);

            string label = day.ToString();
            string callbackData = "ignore";

            if (workingHours != null && date >= currentDate && date <= nextMonthEnd)
            {
                var dayBookings = bookedTimes
                    .Where(b => b.Date == date)
                    .Select(b => b.TimeOfDay)
                    .ToList();

                bool hasAvailableSlot = HasAvailableSlot(workingHours, dayBookings, serviceDuration);

                if (hasAvailableSlot)
                {
                    callbackData = $"choose_date:{date:yyyy-MM-dd}";
                }
                else
                {
                    label += "⚫"; // Fully booked
                }
            }
            else
            {
                label += workingHours == null ? "🚫" : "⚫"; // Not working or no slots
            }

            if (isToday)
                label += "🔵";

            weekRow.Add(InlineKeyboardButton.WithCallbackData(label, callbackData));

            if (weekRow.Count == 7)
            {
                calendarButtons.Add(weekRow);
                weekRow = new List<InlineKeyboardButton>();
            }
        }

        if (weekRow.Any())
            calendarButtons.Add(weekRow);

        var prevButton = selectedDate <= currentMonthStart
            ? InlineKeyboardButton.WithCallbackData("⬅️", "ignore")
            : InlineKeyboardButton.WithCallbackData("⬅️", $"choose_prev_month:{selectedDate:yyyy-MM-dd}");

        var nextButton = selectedDate >= nextMonthStart
            ? InlineKeyboardButton.WithCallbackData("➡️", "ignore")
            : InlineKeyboardButton.WithCallbackData("➡️", $"choose_next_month:{selectedDate:yyyy-MM-dd}");

        calendarButtons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ThisMonth"), $"choose_this_month:{currentDate:yyyy-MM-dd}"),
            prevButton,
            nextButton
        });

        var keyboard = new InlineKeyboardMarkup(calendarButtons);

        await _botClient.SendMessage(
            chatId,
            _translationService.Get(language, "BookingSelectDateHeader", selectedDate),
            replyMarkup: keyboard,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancellationToken);
    }

    private bool HasAvailableSlot(WorkingHours workingHours, List<TimeSpan> dayBookings, TimeSpan serviceDuration)
    {
        var currentTime = workingHours.StartTime;

        while (currentTime + serviceDuration <= workingHours.EndTime)
        {
            var slotEnd = currentTime + serviceDuration;

            bool duringBreak = workingHours.Breaks.Any(b =>
                currentTime < b.EndTime && slotEnd > b.StartTime);

            bool overlapsBooking = dayBookings.Any(booked =>
                booked < slotEnd && booked + serviceDuration > currentTime);

            if (!duringBreak && !overlapsBooking)
                return true;

            currentTime += serviceDuration;
        }

        return false;
    }

    private async Task HandleIgnoreAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var state = _userStateService.GetConversation(chatId);
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

        if (string.IsNullOrEmpty(state))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "NoServiceSelected"), 
                cancellationToken: cancellationToken);
            return;
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "IgnoreCommand"),
            cancellationToken: cancellationToken);
    }

    private async Task HandleDateSelectionAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var state = _userStateService.GetConversation(chatId);
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

        if (string.IsNullOrEmpty(state))
        {
            await _botClient.SendMessage(
                chatId,
                _translationService.Get(language, "NoServiceSelected"),
                cancellationToken: cancellationToken);
            return;
        }

        var serviceId = int.Parse(state.Split('_')[1]);

        var service = await _dbContext.Services
            .Include(s => s.Employee)
                .ThenInclude(e => e.WorkingHours)
                    .ThenInclude(wh => wh.Breaks)
            .Include(s => s.Employee.Company)
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service?.Employee == null || service.Employee.Company == null)
        {
            await _botClient.SendMessage(
                chatId,
                _translationService.Get(language, "NoServiceFound"),
                cancellationToken: cancellationToken);
            return;
        }

        if (!DateTime.TryParse(data, out var userSelectedDate))
        {
            await _botClient.SendMessage(
                chatId,
                _translationService.Get(language, "InvalidDaySelected"),
                cancellationToken: cancellationToken);
            return;
        }

        var dayOfWeek = userSelectedDate.DayOfWeek;
        var workingHours = service.Employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == dayOfWeek);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId,
                _translationService.Get(language, "NotWorkingOnDay"),
                cancellationToken: cancellationToken);
            return;
        }

        var timezoneId = workingHours.Timezone ?? "Europe/Lisbon";
        var companyTimezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);

        var localSelectedDate = DateTime.SpecifyKind(userSelectedDate.Date, DateTimeKind.Unspecified);
        _userStateService.SetConversation(chatId, $"WaitingForTime_{serviceId}_{localSelectedDate:yyyy-MM-dd}_{timezoneId}");

        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localSelectedDate, companyTimezone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(localSelectedDate.AddDays(1), companyTimezone);

        var bookedTimes = await _dbContext.Bookings
            .Where(b => b.ServiceId == serviceId &&
                        b.BookingTime >= dayStartUtc &&
                        b.BookingTime < dayEndUtc)
            .Select(b => b.BookingTime)
            .ToListAsync(cancellationToken);

        var timeSlots = new List<InlineKeyboardButton[]>();
        var currentTime = workingHours.StartTime;
        var currentRow = new List<InlineKeyboardButton>();

        while (currentTime < workingHours.EndTime)
        {
            var slotEnd = currentTime + service.Duration;

            if (slotEnd > workingHours.EndTime)
                break;

            var isDuringBreak = workingHours.Breaks.Any(b =>
                (currentTime >= b.StartTime && currentTime < b.EndTime) ||
                (slotEnd > b.StartTime && slotEnd <= b.EndTime) ||
                (currentTime <= b.StartTime && slotEnd >= b.EndTime));

            if (!isDuringBreak)
            {
                var bookingTimeLocal = localSelectedDate.Add(currentTime);
                var bookingTimeUtc = TimeZoneInfo.ConvertTimeToUtc(bookingTimeLocal, companyTimezone);

                var isBooked = bookedTimes.Any(booked =>
                    bookingTimeUtc < booked + service.Duration &&
                    bookingTimeUtc + service.Duration > booked);

                var isPastTime = bookingTimeUtc <= DateTime.UtcNow;

                if (!isBooked && !isPastTime)
                {
                    currentRow.Add(InlineKeyboardButton.WithCallbackData(
                        currentTime.ToString(@"hh\:mm"),
                        $"choose_time:{(int)currentTime.TotalMinutes}"));

                    if (currentRow.Count == 4)
                    {
                        timeSlots.Add(currentRow.ToArray());
                        currentRow = new List<InlineKeyboardButton>();
                    }
                }
            }

            currentTime += TimeSpan.FromMinutes(30);
        }

        if (currentRow.Any())
        {
            timeSlots.Add(currentRow.ToArray());
        }

        if (!timeSlots.Any())
        {
            _userStateService.SetConversation(chatId, $"WaitingForDate_{serviceId}");
            await _botClient.SendMessage(
                chatId,
                _translationService.Get(language, "NoAvailableTimes"),
                cancellationToken: cancellationToken);
            return;
        }

        timeSlots.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "Back"), "back_to_menu")
        });

        await _botClient.SendMessage(
            chatId,
            _translationService.Get(language, "SelectTime", timezoneId),
            replyMarkup: new InlineKeyboardMarkup(timeSlots),
            cancellationToken: cancellationToken);
    }

    public async Task HandleTimeSelectionAsync(long chatId, string time, CancellationToken cancellationToken)
    {
        var userState = _userStateService.GetConversation(chatId);
        if (!userState.StartsWith("WaitingForTime_"))
            return;

        var parts = userState.Split('_');
        int serviceId = int.Parse(parts[1]);
        DateTime selectedLocalDate = DateTime.Parse(parts[2]);
        string timezoneId = parts[3];

        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

        var service = await _dbContext.Services
            .Include(s => s.Employee)
                .ThenInclude(e => e.Company)
                    .ThenInclude(c => c.Token)
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service == null)
        {
            await _botClient.SendMessage(
                chatId,
                _translationService.Get(language, "NoServiceFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var companyTimezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);

        if (!double.TryParse(time, out var totalMinutes))
        {
            await _botClient.SendMessage(
                chatId,
                _translationService.Get(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }

        var selectedTime = TimeSpan.FromMinutes(totalMinutes);

        var localBookingTime = DateTime.SpecifyKind(selectedLocalDate.Date.Add(selectedTime), DateTimeKind.Unspecified);
        var bookingTimeUtc = TimeZoneInfo.ConvertTimeToUtc(localBookingTime, companyTimezone);

        var client = await _dbContext.Clients.FirstOrDefaultAsync(c => c.ChatId == chatId);
        if (client == null)
        {
            client = new Models.Client { ChatId = chatId, Name = "Test" };
            _dbContext.Clients.Add(client);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var booking = new Booking
        {
            ClientId = client.Id,
            ServiceId = serviceId,
            CompanyId = service.Employee.CompanyId,
            BookingTime = bookingTimeUtc,
            Status = BookingStatus.Pending,
            Service = service,
            Company = service.Employee.Company,
            Client = client
        };

        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _userStateService.RemoveConversation(chatId);

        // --- Client Confirmation ---
        var clientTimezoneId = client.TimeZoneId ?? "Europe/Lisbon";
        var clientTimezone = TimeZoneInfo.FindSystemTimeZoneById(clientTimezoneId);
        var localClientTime = TimeZoneInfo.ConvertTimeFromUtc(bookingTimeUtc, clientTimezone);

        await _botClient.SendMessage(
            chatId: chatId,
            parseMode: ParseMode.MarkdownV2,
            text: _translationService.Get(language, "BookingPendingConfirmation",
                EscapeMarkdownV2(service.Name),
                EscapeMarkdownV2(service.Employee.Name),
                localClientTime.ToString("dddd, MMMM d, yyyy"),
                EscapeMarkdownV2(clientTimezoneId),
                localClientTime.ToString("HH:mm")),
            cancellationToken: cancellationToken);

        // --- Company Notification ---
        var companyOwnerChatId = service.Employee.Company.Token.ChatId;
        if (companyOwnerChatId.HasValue)
        {
            var companyOwnerLanguage = await _userStateService.GetLanguageAsync(companyOwnerChatId.Value, cancellationToken);
            var localCompanyTime = TimeZoneInfo.ConvertTimeFromUtc(bookingTimeUtc, companyTimezone);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        _translationService.Get(companyOwnerLanguage, "Confirm"),
                        $"{CallbackResponses.ConfirmBooking}:{booking.Id}"),
                    InlineKeyboardButton.WithCallbackData(
                        _translationService.Get(companyOwnerLanguage, "Reject"),
                        $"{CallbackResponses.RejectBooking}:{booking.Id}")
                }
            });

            var contactInfo = string.IsNullOrEmpty(client.PhoneNumber)
                ? client.Username
                : client.PhoneNumber;

            var contactDisplay = client.Name + " (@"+contactInfo+")";

            await _botClient.SendMessage(
                chatId: companyOwnerChatId.Value,
                text: _translationService.Get(companyOwnerLanguage, "NewBookingNotification",
                    service.Name,
                    contactDisplay,
                    localCompanyTime.ToString("dddd, MMMM d, yyyy"),
                    localCompanyTime.ToString("HH:mm"),
                    timezoneId),
                replyMarkup: keyboard,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }
    }

    private static string EscapeMarkdownV2(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = Regex.Replace(
            text,
            @"(?<!\\)([_*\[\]()~`>#+=|{}.!-])",
            @"$1"
        );
        return result;
    }

}