
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Enums;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
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

    public ChooseDateTimeCommandHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _dbContext = dbContext;
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
            { "choose_time", HandleTimeSelectionAsync }
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

    public async Task ShowPreviousMonth(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var isParsed = DateTime.TryParse(data, out var parsedDate);
        
        if (!isParsed)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidDaySelected"),
                cancellationToken: cancellationToken);
            return;
        }

        var previousMonth = parsedDate.AddMonths(-1);

        await ShowCalendar(
            chatId: chatId,
            selectedDate: previousMonth,
            cancellationToken: cancellationToken);
    }

    public async Task ShowNextMonth(long chatId, string data, CancellationToken cancellationToken)
    {
        var isParsed = DateTime.TryParse(data, out var parsedDate);
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        if (!isParsed)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidDaySelected"),
                cancellationToken: cancellationToken);
            return;
        }

        var nextMonth = parsedDate.AddMonths(1);

        await ShowCalendar(
            chatId: chatId,
            selectedDate: nextMonth,
            cancellationToken: cancellationToken);
    }

    public async Task ShowCurrentMonth(long chatId, string data, CancellationToken cancellationToken)
    {
        var currentDate = DateTime.UtcNow;

        await ShowCalendar(
            chatId: chatId,
            selectedDate: currentDate,
            cancellationToken: cancellationToken);
    }

    public async Task ShowCalendar(long chatId, DateTime selectedDate, CancellationToken cancellationToken)
    {
        var currentDate = DateTime.UtcNow.Date;
        var currentMonthStart = new DateTime(currentDate.Year, currentDate.Month, 1);
        var nextMonthStart = currentMonthStart.AddMonths(1);
        var nextMonthEnd = nextMonthStart.AddMonths(1).AddDays(-1);

        // Restrict booking window
        if (selectedDate < currentMonthStart || selectedDate > nextMonthEnd)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "⚠️ You can only book for this and next month.",
                cancellationToken: cancellationToken);
            return;
        }

        var state = _userStateService.GetConversation(chatId);
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        if (!state.StartsWith("WaitingForDate_"))
        {
            await _botClient.SendMessage(
                chatId: chatId, 
                text: Translations.GetMessage(language, "NoServiceSelected"),
                cancellationToken: cancellationToken);
            return;
        }

        var serviceId = int.Parse(state.Split('_')[1]);
        var service = await _dbContext.Services
            .Include(s => s.Employee)
                .ThenInclude(e => e.WorkingHours)
                    .ThenInclude(wh => wh.Breaks)
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service == null || service.Employee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId, 
                text: "❌ Service or employee not found.", 
                cancellationToken: cancellationToken);
            return;
        }

        var daysInMonth = DateTime.DaysInMonth(selectedDate.Year, selectedDate.Month);
        var firstDayOfMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
        var startIndex = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;

        var monthStart = DateTime.SpecifyKind(firstDayOfMonth, DateTimeKind.Utc);
        var monthEnd = DateTime.SpecifyKind(firstDayOfMonth.AddMonths(1).AddDays(-1), DateTimeKind.Utc);

        var bookedTimes = await _dbContext.Bookings
            .Where(b => b.ServiceId == serviceId && b.BookingTime >= monthStart && b.BookingTime <= monthEnd)
            .Select(b => b.BookingTime)
            .ToListAsync(cancellationToken);

        List<List<InlineKeyboardButton>> calendarButtons = new();

        calendarButtons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("Mo", "ignore"),
            InlineKeyboardButton.WithCallbackData("Tu", "ignore"),
            InlineKeyboardButton.WithCallbackData("We", "ignore"),
            InlineKeyboardButton.WithCallbackData("Th", "ignore"),
            InlineKeyboardButton.WithCallbackData("Fr", "ignore"),
            InlineKeyboardButton.WithCallbackData("Sa", "ignore"),
            InlineKeyboardButton.WithCallbackData("Su", "ignore"),
        });

        List<InlineKeyboardButton> weekRow = new();

        for (int i = 0; i < startIndex; i++)
            weekRow.Add(InlineKeyboardButton.WithCallbackData(" ", "ignore"));

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = DateTime.SpecifyKind(new DateTime(selectedDate.Year, selectedDate.Month, day), DateTimeKind.Utc);
            var isToday = date.Date == currentDate;
            var workingHours = service.Employee.WorkingHours.FirstOrDefault(wh => wh.DayOfWeek == date.DayOfWeek);

            string label = day.ToString();
            string callbackData = "ignore";

            if (workingHours != null && date.Date >= currentDate && date <= nextMonthEnd)
            {
                var dayBookings = bookedTimes.Where(b => b.Date == date.Date).Select(b => b.TimeOfDay).ToList();
                var currentTime = workingHours.StartTime;
                bool hasAvailableSlot = false;

                while (currentTime < workingHours.EndTime)
                {
                    var slotEnd = currentTime.Add(service.Duration);

                    if (slotEnd > workingHours.EndTime)
                        break;

                    bool isDuringBreak = workingHours.Breaks.Any(b =>
                        (currentTime >= b.StartTime && currentTime < b.EndTime) ||
                        (slotEnd > b.StartTime && slotEnd <= b.EndTime) ||
                        (currentTime <= b.StartTime && slotEnd >= b.EndTime));

                    bool isBooked = dayBookings.Any(bookedTime =>
                        (currentTime <= bookedTime && bookedTime < slotEnd) ||
                        (bookedTime <= currentTime && currentTime < bookedTime.Add(service.Duration)));

                    if (!isDuringBreak && !isBooked)
                    {
                        hasAvailableSlot = true;
                        break;
                    }

                    currentTime = currentTime.Add(service.Duration);
                }

                if (hasAvailableSlot)
                {
                    callbackData = $"choose_date:{date:yyyy-MM-dd}";
                }
                else
                {
                    label += "⚫";
                }
            }
            else
            {
                label += workingHours == null ? "🚫" : "⚫";
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
            InlineKeyboardButton.WithCallbackData("📆 This Month", $"choose_this_month:{currentDate:yyyy-MM-dd}"),
            prevButton,
            nextButton
        });

        var keyboard = new InlineKeyboardMarkup(calendarButtons);

        string messageText = $"📅 Select a date: {selectedDate:MMMM yyyy}\n" +
                            "🔵 Today | ⚫ Fully booked | 🚫 Day off\n" +
                            "You can book appointments only for this and next month.";

        await _botClient.SendMessage(
            chatId: chatId, 
            text: messageText, 
            replyMarkup: keyboard, 
            cancellationToken: cancellationToken);
    }

   private async Task HandleDateSelectionAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var state = _userStateService.GetConversation(chatId);
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

        if (string.IsNullOrEmpty(state))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoServiceSelected"),
                cancellationToken: cancellationToken);
            return;
        }

        var serviceId = int.Parse(state.Split('_')[1]);

        // Load service, employee, working hours
        var service = await _dbContext.Services
            .Include(s => s.Employee)
                .ThenInclude(e => e.WorkingHours)
                    .ThenInclude(wh => wh.Breaks)
            .Include(s => s.Employee.Company)
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service?.Employee == null || service.Employee.Company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoServiceFound"),
                cancellationToken: cancellationToken);
            return;
        }

        if (!DateTime.TryParse(data, out var userSelectedDate))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidDaySelected"),
                cancellationToken: cancellationToken);
            return;
        }

        // Find working hours for selected day
        var dayOfWeek = userSelectedDate.DayOfWeek;
        var workingHours = service.Employee.WorkingHours
            .FirstOrDefault(wh => wh.DayOfWeek == dayOfWeek);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NotWorkingOnDay"),
                cancellationToken: cancellationToken);
            return;
        }

        // Get timezone for that day's working hours
        var timezoneId = workingHours.Timezone ?? "Europe/Lisbon";
        var companyTimezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);

        // Treat selected date as local in company timezone
        var localSelectedDate = DateTime.SpecifyKind(userSelectedDate.Date, DateTimeKind.Unspecified);

        // Save state with date and timezone
        _userStateService.SetConversation(chatId, $"WaitingForTime_{serviceId}_{localSelectedDate:yyyy-MM-dd}_{timezoneId}");

        // Convert selected local day to UTC range
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localSelectedDate, companyTimezone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(localSelectedDate.AddDays(1), companyTimezone);

        // Find existing bookings in UTC
        var bookedTimes = await _dbContext.Bookings
            .Where(b => b.ServiceId == serviceId &&
                        b.BookingTime >= dayStartUtc &&
                        b.BookingTime < dayEndUtc)
            .Select(b => b.BookingTime)
            .ToListAsync(cancellationToken);

        // Generate time slots
        var timeSlots = new List<InlineKeyboardButton[]>();
        var currentTime = workingHours.StartTime;
        var currentHourGroup = new List<InlineKeyboardButton>();

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
                    (bookingTimeUtc <= booked && booked < bookingTimeUtc + service.Duration) ||
                    (booked <= bookingTimeUtc && bookingTimeUtc < booked + service.Duration));

                var isPastTime = bookingTimeUtc <= DateTime.UtcNow;

                if (!isBooked && !isPastTime)
                {
                    currentHourGroup.Add(InlineKeyboardButton.WithCallbackData(
                        currentTime.ToString(@"hh\:mm"),
                        $"choose_time:{currentTime}"));

                    if (currentHourGroup.Count == 4 || currentTime + TimeSpan.FromMinutes(30) >= workingHours.EndTime)
                    {
                        timeSlots.Add(currentHourGroup.ToArray());
                        currentHourGroup = new List<InlineKeyboardButton>();
                    }
                }
            }

            currentTime += TimeSpan.FromMinutes(30);
        }

        if (currentHourGroup.Any())
        {
            timeSlots.Add(currentHourGroup.ToArray());
        }

        if (!timeSlots.Any())
        {
            _userStateService.SetConversation(chatId, $"WaitingForDate_{serviceId}");
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoAvailableTimes"),
                cancellationToken: cancellationToken);
            return;
        }

        timeSlots.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), "back_to_menu")
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SelectTime", timezoneId),
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
                chatId: chatId,
                text: Translations.GetMessage(language, "NoServiceFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var companyTimezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);

        TimeSpan selectedTime = TimeSpan.Parse(time);

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

        // Send confirmation to Client
        var clientTimezoneId = client.TimeZoneId ?? "Europe/Lisbon"; // safe fallback
        var clientTimezone = TimeZoneInfo.FindSystemTimeZoneById(clientTimezoneId);
        var localClientTime = TimeZoneInfo.ConvertTimeFromUtc(bookingTimeUtc, clientTimezone);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BookingPendingConfirmation",
                service.Name,
                service.Employee.Name,
                localClientTime.ToString("dddd, MMMM d, yyyy"),
                clientTimezoneId,
                localClientTime.ToString("HH:mm")),
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancellationToken);

        // Send notification to Company Owner
        var companyOwnerChatId = service.Employee.Company.Token.ChatId;
        if (companyOwnerChatId.HasValue)
        {
            var companyOwnerLanguage = await _userStateService.GetLanguageAsync(companyOwnerChatId.Value, cancellationToken);
            var localCompanyTime = TimeZoneInfo.ConvertTimeFromUtc(bookingTimeUtc, companyTimezone);

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(Translations.GetMessage(companyOwnerLanguage, "Confirm"), $"{CallbackResponses.ConfirmBooking}:{booking.Id}"),
                    InlineKeyboardButton.WithCallbackData(Translations.GetMessage(companyOwnerLanguage, "Reject"), $"{CallbackResponses.RejectBooking}:{booking.Id}")
                }
            });

            await _botClient.SendMessage(
                chatId: companyOwnerChatId.Value,
                text: Translations.GetMessage(companyOwnerLanguage, "NewBookingNotification",
                    service.Name,
                    client.Name,
                    localCompanyTime.ToString("dddd, MMMM d, yyyy"),
                    localCompanyTime.ToString("HH:mm"),
                    timezoneId),
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
    }
}