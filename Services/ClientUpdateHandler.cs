using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Examples.WebHook;
using Telegram.Bot.Examples.WebHook.Models;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Examples.WebHook.Services;
using System.Collections.Concurrent;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Examples.WebHook.Services.Constants;

namespace Telegram.Bot.Services;
public class ClientUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly UserStateService _userStateService;
    private readonly ConcurrentDictionary<long, string> userLanguages = new ConcurrentDictionary<long, string>();

    public ClientUpdateHandler(
        ITelegramBotClient botClient,
        BookingDbContext dbContext,
        UserStateService userStateService)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _userStateService = userStateService;
    }

    public async Task StartClientFlow(long chatId, int companyId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }

        // Show main menu
        await ShowMainMenu(chatId, companyId, cancellationToken);
    }

    private async Task ShowMainMenu(long chatId, int companyId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var buttons = new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BookAppointment"), "book_appointment") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "MyBookings"), "view_bookings") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ChangeLanguage"), "change_language") }
        };

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "WelcomeToCompany", company.Name),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        if (update == null) return;

        var handler = update switch
        {
            { Message: { } message } => BotOnMessageReceived(message, cancellationToken),
            //{ EditedMessage: { } message } => BotOnMessageReceived(message, cancellationToken),
            { CallbackQuery: { } callbackQuery } => BotOnCallbackQueryReceived(callbackQuery, cancellationToken),
            _ => Task.CompletedTask
        };

        await handler;
    }

    private async Task BotOnMessageReceived(Message message, CancellationToken cancellationToken)
    {
        if (message?.Text is not { } messageText) return;

        var chatId = message.Chat.Id;
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
        // Check if this is a new user or a command
        if (messageText.StartsWith("/start") || messageText.StartsWith("/menu"))
        {
            // Get client's most recent booking to determine company
            var client = await _dbContext.Clients
                .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

            if (client == null)
            {
                // New user - show company selection
                await HandleBookAppointment(chatId, cancellationToken);
                return;
            }

            // Get the most recent booking for this client
            var recentBooking = await _dbContext.Bookings
                .Where(b => b.ClientId == client.Id)
                .OrderByDescending(b => b.BookingTime)
                .FirstOrDefaultAsync(cancellationToken);

            if (recentBooking == null)
            {
                // No bookings yet - show company selection
                await HandleBookAppointment(chatId, cancellationToken);
                return;
            }

            // Get user state with the correct company ID
            var userState = _userStateService.GetOrCreate(chatId, recentBooking.CompanyId);
            await ShowMainMenu(chatId, userState.CompanyId, cancellationToken);
            return;
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "UseMenuButton"),
            cancellationToken: cancellationToken);
    }

    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null) return;
        var chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data;
        var language = userLanguages.GetValueOrDefault(chatId, "EN");

        switch (data)
        {
            case "book_appointment":
                await HandleBookAppointment(chatId, cancellationToken);
                break;
            case "view_bookings":
                await HandleViewBookings(chatId, cancellationToken);
                break;
            case "change_language":
                var languageKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("English", "set_language:EN") },
                    new[] { InlineKeyboardButton.WithCallbackData("Українська", "set_language:UA") }
                });

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "SelectLanguage"),
                    replyMarkup: languageKeyboard,
                    cancellationToken: cancellationToken);
                break;
            case var s when s.StartsWith("set_language:"):
                var selectedLanguage = s.Split(':')[1];
                await SetLanguage(chatId, selectedLanguage, cancellationToken);
                break;
            case "back_to_menu":
                // Get client's most recent booking to determine company
                var client = await _dbContext.Clients
                    .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

                if (client != null)
                {
                    var recentBooking = await _dbContext.Bookings
                        .Where(b => b.ClientId == client.Id)
                        .OrderByDescending(b => b.BookingTime)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (recentBooking != null)
                    {
                        var userState = _userStateService.GetOrCreate(chatId, recentBooking.CompanyId);
                        await ShowMainMenu(chatId, userState.CompanyId, cancellationToken);
                    }
                    else
                    {
                        await HandleBookAppointment(chatId, cancellationToken);
                    }
                }
                else
                {
                    await HandleBookAppointment(chatId, cancellationToken);
                }
                break;
            case var s when s.StartsWith("category_"):
                var category = data.Replace("category_", "");
                await HandleCategorySelection(chatId, category, cancellationToken);
                break;
            case var s when s.StartsWith("company_"):
                var companyId = int.Parse(data.Replace("company_", ""));
                await HandleCompanySelection(chatId, companyId, cancellationToken);
                break;
            case var s when s.StartsWith("service_"):
                var serviceId = int.Parse(data.Replace("service_", ""));
                await HandleServiceSelection(chatId, serviceId, cancellationToken);
                break;
            case var s when s.StartsWith("date_"):
                var selectedDate = DateTime.Parse(data.Replace("date_", ""));
                await HandleDateSelection(chatId, selectedDate, cancellationToken);
                break;
            case var s when s.StartsWith("prev_") || s.StartsWith("next_"):
                var referenceDate = DateTime.Parse(data.Replace("prev_", "").Replace("next_", ""));
                DateTime newMonth = data.StartsWith("prev_") 
                    ? referenceDate.AddMonths(-1) 
                    : referenceDate.AddMonths(1);
                await ShowCalendar(chatId, newMonth, cancellationToken);
                break;
            case var s when s.StartsWith("time_"):
                var time = data.Replace("time_", "");
                await HandleTimeSelection(chatId, time, cancellationToken);
                break;
        }
    }

    private async Task HandleBookAppointment(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var companies = await _dbContext.Companies.ToListAsync(cancellationToken);

        if (!companies.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompaniesAvailable"),
                cancellationToken: cancellationToken);
            return;
        }

        var companyButtons = companies.Select(c =>
            new[] { InlineKeyboardButton.WithCallbackData(c.Name, $"company_{c.Id}") }).ToArray();

        var keyboard = new InlineKeyboardMarkup(companyButtons.Concat(new[] 
        { 
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), "back_to_menu") }
        }));

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SelectCompany"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleViewBookings(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (client == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoBookingsFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var bookings = await _dbContext.Bookings
            .Include(b => b.Service)
                .ThenInclude(s => s.Employee)
            .Where(b => b.ClientId == client.Id && b.BookingTime >= DateTime.UtcNow)
            .OrderBy(b => b.BookingTime)
            .ToListAsync(cancellationToken);

        if (!bookings.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoUpcomingBookings"),
                replyMarkup: new InlineKeyboardMarkup(new[] 
                { 
                    new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), "back_to_menu") }
                }),
                cancellationToken: cancellationToken);
            return;
        }

        var message = Translations.GetMessage(language, "UpcomingBookings") + "\n\n";
        foreach (var booking in bookings)
        {
            var localTime = booking.BookingTime.ToLocalTime();
            message += Translations.GetMessage(language, "BookingDetails", 
                booking.Service.Name, 
                booking.Service.Employee.Name,
                localTime.ToString("dddd, MMMM d, yyyy"),
                localTime.ToString("hh:mm tt")) + "\n\n";
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: message,
            replyMarkup: new InlineKeyboardMarkup(new[] 
            { 
                new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), "back_to_menu") }
            }),
            cancellationToken: cancellationToken);
    }

    public async Task HandleCategorySelection(long chatId, string category, CancellationToken cancellationToken)
    {
        var companies = await _dbContext.Companies
        // .Where(c => c.Category == category)
            .ToListAsync(cancellationToken);

        if (!companies.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ No companies found for this category. Please try another.",
                cancellationToken: cancellationToken);
            return;
        }

        var companyButtons = companies.Select(c =>
            new[] { InlineKeyboardButton.WithCallbackData(c.Name, $"company_{c.Id}") }).ToArray();

        InlineKeyboardMarkup companyKeyboard = new(companyButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: "Here are the companies offering this service:",
            replyMarkup: companyKeyboard,
            cancellationToken: cancellationToken);
    }

    private static ConcurrentDictionary<long, string> userConversations = new ConcurrentDictionary<long, string>();

    public async Task HandleCompanySelection(long chatId, int companyId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
            return;
        }
        var services = await (from s in _dbContext.Services
                      join e in _dbContext.Employees on s.EmployeeId equals e.Id
                      where e.CompanyId == company.Id
                      select s).ToListAsync(cancellationToken);


        if (services == null || !services.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoServicesAvailable", company.Name),
                cancellationToken: cancellationToken);
            return;
        }

        // Generate buttons for services
        var serviceButtons = services
            .Select(service => new[]
            {
                InlineKeyboardButton.WithCallbackData(service.Name, $"service_{service.Id}")
            })
            .ToArray();

        InlineKeyboardMarkup serviceKeyboard = new(serviceButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "CompanyServices", company.Name),
            replyMarkup: serviceKeyboard,
            cancellationToken: cancellationToken);
    }
    public async Task HandleServiceSelection(long chatId, int serviceId, CancellationToken cancellationToken)
    {
        var service = await _dbContext.Services
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Service not found. Please try again",
                cancellationToken: cancellationToken);
            return;
        }

        userConversations[chatId] = $"WaitingForDate_{serviceId}";
        await ShowCalendar(chatId, DateTime.UtcNow, cancellationToken);
    }

    public async Task HandleDateSelection(long chatId, DateTime selectedDate, CancellationToken cancellationToken)
    {
        if (!userConversations.ContainsKey(chatId) || !userConversations[chatId].StartsWith("WaitingForDate_"))
            return;

        var serviceId = int.Parse(userConversations[chatId].Split('_')[1]);
        
        // Get service with employee and working hours details
        var service = await _dbContext.Services
            .Include(s => s.Employee)
                .ThenInclude(e => e.WorkingHours)
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service == null || service.Employee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Service or employee not found. Please try again.",
                cancellationToken: cancellationToken);
            return;
        }

        var employee = service.Employee;
        var selectedDateUtc = DateTime.SpecifyKind(selectedDate, DateTimeKind.Utc);
        var dayOfWeek = selectedDateUtc.DayOfWeek;

        // Get working hours for the selected day of week
        var workingHours = employee.WorkingHours
            .FirstOrDefault(wh => wh.DayOfWeek == dayOfWeek);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"❌ {employee.Name} is not working on {selectedDate:dddd}. Please select another date.",
                cancellationToken: cancellationToken);
            return;
        }

        // Update user state for time selection
        userConversations[chatId] = $"WaitingForTime_{serviceId}_{selectedDate:yyyy-MM-dd}";

        // Get all bookings for this service on the selected date
        var dayStart = selectedDateUtc.Date;
        var dayEnd = dayStart.AddDays(1);

        var bookedTimes = await _dbContext.Bookings
            .Where(b => b.ServiceId == serviceId && 
                        b.BookingTime >= dayStart && 
                        b.BookingTime < dayEnd)
            .Select(b => b.BookingTime)
            .ToListAsync(cancellationToken);

        // Generate available time slots based on employee working hours
        var timeSlots = new List<InlineKeyboardButton[]>();
        var currentTime = DateTime.UtcNow;

        // Use employee's working hours
        var startHour = workingHours.StartTime;
        var endHour = workingHours.EndTime;
        var breakTime = workingHours.BreakTime;

        // Generate time slots every hour
        for (var hour = startHour.Hours; hour < endHour.Hours; hour++)
        {
            var timeSlot = new TimeSpan(hour, 0, 0);
            var slotDateTime = DateTime.SpecifyKind(selectedDate.Date + timeSlot, DateTimeKind.Utc);

            // Skip if the time slot is in the past
            if (slotDateTime <= currentTime)
                continue;

            // Skip if the time slot is already booked
            if (bookedTimes.Any(bt => bt.Hour == hour))
                continue;

            // Check if there's enough time for the service and break
            var serviceEndTime = slotDateTime.Add(service.Duration);
            var nextAvailableTime = serviceEndTime.Add(breakTime);

            // Skip if the service would end after working hours
            if (serviceEndTime.TimeOfDay > endHour)
                continue;

            // Format time for display (convert to local time)
            var localTime = slotDateTime.ToLocalTime();
            var timeDisplay = localTime.ToString("h:mm tt");
            var timeValue = timeSlot.ToString(@"hh\:mm");

            timeSlots.Add(new[] { InlineKeyboardButton.WithCallbackData(timeDisplay, $"time_{timeValue}") });
        }

        if (!timeSlots.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"❌ No available time slots for {selectedDate:yyyy-MM-dd}. Please select another date.",
                cancellationToken: cancellationToken);
            return;
        }

        var keyboard = new InlineKeyboardMarkup(timeSlots);

        await _botClient.SendMessage(
            chatId: chatId,
            text: $"⏰ Select a time slot for {selectedDate:yyyy-MM-dd} with {employee.Name}:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    public async Task HandleTimeSelection(long chatId, string time, CancellationToken cancellationToken)
    {
        if (!userConversations.ContainsKey(chatId))
            return;

        var userState = userConversations[chatId];
        if (!userState.StartsWith("WaitingForTime_"))
            return;

        var parts = userState.Split('_');
        int serviceId = int.Parse(parts[1]);
        DateTime selectedDate = DateTime.Parse(parts[2]);
        var language = userLanguages.GetValueOrDefault(chatId, "EN");

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

        // Parse the time from string and create a TimeSpan
        TimeSpan selectedTime = TimeSpan.Parse(time);

        // Create DateTime in UTC
        DateTime bookingTime = DateTime.SpecifyKind(selectedDate.Date + selectedTime, DateTimeKind.Utc);

        var client = _dbContext.Clients.FirstOrDefault(c => c.ChatId == chatId);

        if (client == null)
        {
            client = new Client { ChatId = chatId, Name = "Test" };
            _dbContext.Clients.Add(client);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var clientId = client.Id;

        // Save booking in database
        var booking = new Booking
        {
            ClientId = clientId,
            ServiceId = serviceId,
            CompanyId = service.Employee.CompanyId,
            BookingTime = bookingTime,
        };

        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);

        userConversations.TryRemove(chatId, out _);

        // Convert UTC time back to local time for display
        var localBookingTime = bookingTime.ToLocalTime();
        
        // Send confirmation message to client
        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BookingConfirmation",
                service.Name,
                service.Employee.Name,
                localBookingTime.ToString("dddd, MMMM d, yyyy"),
                localBookingTime.ToString("hh:mm tt")),
            cancellationToken: cancellationToken);

        // Send notification to company owner
        var companyOwnerChatId = service.Employee.Company.Token.ChatId;
        if (companyOwnerChatId.HasValue)
        {
            var companyOwnerLanguage = userLanguages.GetValueOrDefault<long, string>(companyOwnerChatId.Value, "EN");
            
            await _botClient.SendMessage(
                chatId: companyOwnerChatId.Value,
                text: Translations.GetMessage(companyOwnerLanguage, "NewBookingNotification",
                    service.Name,
                    client.Name,
                    localBookingTime.ToString("dddd, MMMM d, yyyy"),
                    localBookingTime.ToString("hh:mm tt")),
                cancellationToken: cancellationToken);
        }

        // Show main menu after successful booking
        var userStateObj = _userStateService.GetOrCreate(chatId, service.Employee.CompanyId);
        await ShowMainMenu(chatId, userStateObj.CompanyId, cancellationToken);
    }

    public async Task ShowCalendar(long chatId, DateTime selectedDate, CancellationToken cancellationToken)
    {
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
            InlineKeyboardButton.WithCallbackData("Mo", "ignore"),
            InlineKeyboardButton.WithCallbackData("Tu", "ignore"),
            InlineKeyboardButton.WithCallbackData("We", "ignore"),
            InlineKeyboardButton.WithCallbackData("Th", "ignore"),
            InlineKeyboardButton.WithCallbackData("Fr", "ignore"),
            InlineKeyboardButton.WithCallbackData("Sa", "ignore"),
            InlineKeyboardButton.WithCallbackData("Su", "ignore"),
        });

        List<InlineKeyboardButton> weekRow = new();

        // Add empty buttons before the first day of the month
        for (int i = 1; i < (int)firstDayOfMonth.DayOfWeek; i++)
        {
            weekRow.Add(InlineKeyboardButton.WithCallbackData(" ", "ignore"));
        }

        // Get the service ID from the user conversation state
        if (!userConversations.TryGetValue(chatId, out var state) || !state.StartsWith("WaitingForDate_"))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: No service selected. Please try again.",
                cancellationToken: cancellationToken);
            return;
        }

        var serviceId = int.Parse(state.Split('_')[1]);
        var service = await _dbContext.Services
            .Include(s => s.Employee)
                .ThenInclude(e => e.WorkingHours)
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service == null || service.Employee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: Service or employee not found. Please try again.",
                cancellationToken: cancellationToken);
            return;
        }

        // Get all bookings for this service in the current month
        var monthStart = DateTime.SpecifyKind(new DateTime(selectedDate.Year, selectedDate.Month, 1), DateTimeKind.Utc);
        var monthEnd = DateTime.SpecifyKind(monthStart.AddMonths(1).AddDays(-1), DateTimeKind.Utc);
        
        var bookedDates = await _dbContext.Bookings
            .Where(b => b.ServiceId == serviceId && 
                        b.BookingTime >= monthStart && 
                        b.BookingTime <= monthEnd)
            .Select(b => b.BookingTime.Date)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Generate day buttons
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = DateTime.SpecifyKind(new DateTime(selectedDate.Year, selectedDate.Month, day), DateTimeKind.Utc);
            var isBooked = bookedDates.Contains(date.Date);
            var isPastDate = date.Date < currentDate;
            var dayOfWeek = date.DayOfWeek;
            
            // Check if employee works on this day
            var workingHours = service.Employee.WorkingHours
                .FirstOrDefault(wh => wh.DayOfWeek == dayOfWeek);
            var isWorkingDay = workingHours != null;
            
            if (isBooked || isPastDate || isFutureMonth || !isWorkingDay)
            {
                // Add disabled button with small dot for unavailable dates
                weekRow.Add(InlineKeyboardButton.WithCallbackData($"{day}⚫", "ignore"));
            }
            else
            {
                // Add enabled button with just the number
                weekRow.Add(InlineKeyboardButton.WithCallbackData(day.ToString(), $"date_{date:yyyy-MM-dd}"));
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
            ? InlineKeyboardButton.WithCallbackData("⬅️", "ignore")
            : InlineKeyboardButton.WithCallbackData("⬅️", $"prev_{selectedDate:yyyy-MM-dd}");

        var nextButton = isFutureMonth || isNextMonth
            ? InlineKeyboardButton.WithCallbackData("➡️", "ignore")
            : InlineKeyboardButton.WithCallbackData("➡️", $"next_{selectedDate:yyyy-MM-dd}");

        calendarButtons.Add(new List<InlineKeyboardButton>
        {
            prevButton,
            nextButton
        });

        var keyboard = new InlineKeyboardMarkup(calendarButtons);

        var messageText = $"📅 Select a date: {selectedDate:MMMM yyyy}\n" +
                         (isPastMonth ? "⚠️ Past month\n" : "") +
                         (isFutureMonth ? "⚠️ Future month\n" : "") +
                         "Available dates are clickable\n" +
                         "⚫ Unavailable dates (booked, past, or non-working days)";

        await _botClient.SendMessage(
            chatId: chatId,
            text: messageText,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task SetLanguage(long chatId, string language, CancellationToken cancellationToken)
    {
        userLanguages[chatId] = language;
        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "LanguageSet", language),
            cancellationToken: cancellationToken);
        
        // Get client's most recent booking to determine company
        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (client != null)
        {
            var recentBooking = await _dbContext.Bookings
                .Where(b => b.ClientId == client.Id)
                .OrderByDescending(b => b.BookingTime)
                .FirstOrDefaultAsync(cancellationToken);

            if (recentBooking != null)
            {
                var userState = _userStateService.GetOrCreate(chatId, recentBooking.CompanyId);
                await ShowMainMenu(chatId, userState.CompanyId, cancellationToken);
            }
            else
            {
                await HandleBookAppointment(chatId, cancellationToken);
            }
        }
        else
        {
            await HandleBookAppointment(chatId, cancellationToken);
        }
    }
}
