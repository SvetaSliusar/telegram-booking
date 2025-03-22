using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Examples.WebHook;
using Telegram.Bot.Examples.WebHook.Models;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Examples.WebHook.Services;
using System.Collections.Concurrent;

namespace Telegram.Bot.Services;
public class ClientUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly UserStateService _userStateService;

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
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Company not found. Please try again.",
                cancellationToken: cancellationToken);
            return;
        }

        var services = await (from s in _dbContext.Services
                            join e in _dbContext.Employees on s.EmployeeId equals e.Id
                            where e.CompanyId == companyId
                            select s).ToListAsync(cancellationToken);

        if (!services.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"❌ No services available for {company.Name}.",
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

        // Initialize or update user state
        var userState = _userStateService.GetOrCreate(chatId, companyId);
        userState.CurrentStep = ConversationStep.SelectingService;
        _userStateService.SetState(chatId, userState);

        await _botClient.SendMessage(
            chatId: chatId,
            text: $"📋 Services offered by {company.Name}:",
            replyMarkup: serviceKeyboard,
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
        await _botClient.SendMessage(
            chatId: chatId,
            text: "Don't understand your message. Please use the provided buttons.",
            cancellationToken: cancellationToken);
    }


    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null) return;
        var chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data;

        if (data.StartsWith("category_"))
        {
            var category = data.Replace("category_", "");
            await HandleCategorySelection(chatId, category, cancellationToken);
        }
        else if (data.StartsWith("company_"))
        {
            var companyId = int.Parse(data.Replace("company_", ""));
            await HandleCompanySelection(chatId, companyId, cancellationToken);
        }
        else if (data.StartsWith("service_"))
        {
            var serviceId = int.Parse(data.Replace("service_", ""));
            await HandleServiceSelection(chatId, serviceId, cancellationToken);
        }
        else if (data.StartsWith("date_"))
        {
            var selectedDate = DateTime.Parse(data.Replace("date_", ""));
            await HandleDateSelection(chatId, selectedDate, cancellationToken);
        }
        else if (data.StartsWith("prev_") || data.StartsWith("next_"))
        {
            var referenceDate = DateTime.Parse(data.Replace("prev_", "").Replace("next_", ""));
            DateTime newMonth = data.StartsWith("prev_") 
                ? referenceDate.AddMonths(-1) 
                : referenceDate.AddMonths(1);

            await ShowCalendar(chatId, newMonth, cancellationToken);
        }

        else if (data.StartsWith("time_"))
        {
            var time = data.Replace("time_", "");
            await HandleTimeSelection(chatId, time, cancellationToken);
        }

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
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Company not found. Please try again.",
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
                text: $"❌ No services available for {company.Name}.",
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
            text: $"📋 Services offered by {company.Name}:",
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
        userConversations[chatId] = $"WaitingForTime_{serviceId}_{selectedDate:yyyy-MM-dd}";

        InlineKeyboardMarkup timeSlots = new(
            new[]
            {
                new [] { InlineKeyboardButton.WithCallbackData("10:00 AM", "time_10:00") },
                new [] { InlineKeyboardButton.WithCallbackData("12:00 PM", "time_12:00") },
                new [] { InlineKeyboardButton.WithCallbackData("2:00 PM", "time_14:00") }
            });

        await _botClient.SendMessage(
            chatId: chatId,
            text: $"⏰ Select a time slot for {selectedDate:yyyy-MM-dd}:",
            replyMarkup: timeSlots,
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

        var service = await _dbContext.Services
            .Include(s => s.Employee)
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Service not found. Please try again.",
                cancellationToken: cancellationToken);
            return;
        }

        // Parse the time from string and create a TimeSpan
        TimeSpan selectedTime = TimeSpan.Parse(time); // Example: "10:00" -> 10 hours, 0 minutes

        // Combine Date + Time
        DateTime bookingTime = selectedDate.Date + selectedTime; // Keeps only date + time, removes time zone

        // Convert to UTC
        DateTime bookingTimeUtc = DateTime.SpecifyKind(bookingTime, DateTimeKind.Utc);

        var client = _dbContext.Clients.FirstOrDefault(c => c.ChatId == chatId);

        if (client == null)
        {
            client = new Client { ChatId = chatId, Name = "Test" };
            _dbContext.Clients.Add(client);
            await _dbContext.SaveChangesAsync(cancellationToken); // Ensure the Client ID is generated
        }

        var clientId = client.Id; // Now we have a valid client ID

        // Save booking in database
        var booking = new Booking
        {
            ClientId = clientId,
            ServiceId = serviceId,
            BookingTime = bookingTimeUtc,
        };

        _dbContext.Bookings.Add(booking);


        userConversations.TryRemove(chatId, out _);

        await _botClient.SendMessage(
            chatId: chatId,
            text: $"✅ Your booking for {service.Name} with {service.Employee.Name} on {selectedDate:yyyy-MM-dd} at {time} has been confirmed!",
            cancellationToken: cancellationToken);

        // // Notify company
        // await _botClient.SendMessage(
        //     chatId: service.Company.ChatId ?? 0, // Notify company if chat ID exists
        //     text: $"📢 New Booking: {service.ServiceName} booked on {selectedDate:yyyy-MM-dd} at {time}!",
        //     cancellationToken: cancellationToken);
    }

    public async Task ShowCalendar(long chatId, DateTime selectedDate, CancellationToken cancellationToken)
    {
        var daysInMonth = DateTime.DaysInMonth(selectedDate.Year, selectedDate.Month);
        var firstDayOfMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);

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

        // Generate day buttons
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(selectedDate.Year, selectedDate.Month, day);
            weekRow.Add(InlineKeyboardButton.WithCallbackData(day.ToString(), $"date_{date:yyyy-MM-dd}"));

            if (weekRow.Count == 7) // New row after each week
            {
                calendarButtons.Add(weekRow);
                weekRow = new List<InlineKeyboardButton>();
            }
        }

        if (weekRow.Any()) // Add last row if not full
            calendarButtons.Add(weekRow);

        // Navigation buttons
        calendarButtons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("⬅️ Prev", $"prev_{selectedDate:yyyy-MM-dd}"),
            InlineKeyboardButton.WithCallbackData("➡️ Next", $"next_{selectedDate:yyyy-MM-dd}")
        });

        var keyboard = new InlineKeyboardMarkup(calendarButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: $"📅 Select a date: {selectedDate:MMMM yyyy}",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}
