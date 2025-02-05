using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Examples.WebHook;
using Telegram.Bot.Examples.WebHook.Enums;
using Telegram.Bot.Examples.WebHook.Models;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;
using System.Globalization;
using Telegram.Bot.Examples.WebHook.Services.Constants;
using Telegram.Bot.Examples.WebHook.Services;
using Telegram.Bot.Examples.WebHook.Models;

namespace Telegram.Bot.Services;
public class ClientUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly BookingDbContext _dbContext;
    private readonly ILogger<ClientUpdateHandler> _logger;
    private readonly UserStateService _userStateService;

    public ClientUpdateHandler(
        ITelegramBotClient botClient,
        BookingDbContext dbContext,
        ILogger<ClientUpdateHandler> logger,
        UserStateService userStateService)
    {
        _botClient = botClient;
        _dbContext = dbContext;
        _logger = logger;
        _userStateService = userStateService;
    }

    public async Task HandleUpdateAsync(Update update, Token token, CancellationToken cancellationToken)
    {
        if (token?.CompanyId == null)
        {
            var chatId = update.Type switch
            {
                UpdateType.Message => update.Message?.Chat.Id,
                UpdateType.CallbackQuery => update.CallbackQuery?.Message?.Chat.Id,
                _ => null
            };

            if (chatId.HasValue)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId.Value,
                    text: "Invalid company token. Please contact support.",
                    cancellationToken: cancellationToken);
            }
            return;
        }

        var handler = update.Type switch
        {
            UpdateType.Message => HandleMessageAsync(update.Message!, token, cancellationToken),
            UpdateType.CallbackQuery => HandleCallbackQueryAsync(update.CallbackQuery!, cancellationToken),
            _ => Task.CompletedTask
        };

        await handler;
    }

    private async Task HandleMessageAsync(Message message, Token token, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;

        if (message.Text == "/start")
        {
            if (token?.CompanyId == null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Invalid company token. Please contact support.",
                    cancellationToken: cancellationToken);
                return;
            }

            // Just create the state without storing the return value
            _userStateService.GetOrCreate(chatId, token.CompanyId.Value);
            await SendLanguageSelectionAsync(chatId, cancellationToken);
            return;
        }

        // Get existing state for all other messages
        if (!_userStateService.TryGetState(chatId, out var currentState))
        {
            _logger.LogInformation("No state found for chatId: {ChatId}", chatId);
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Please start over with /start",
                cancellationToken: cancellationToken);
            return;
        }

        _logger.LogInformation("Processing step {Step} for chatId: {ChatId}", currentState.CurrentStep, chatId);

        switch (currentState.CurrentStep)
        {
            case ConversationStep.AwaitingLanguage:
                if (message.Text == "English" || message.Text == "Українська")
                {
                    await HandleLanguageSelectionAsync(chatId, message.Text, currentState, cancellationToken);
                }
                else
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Please select a language using the provided buttons. / Будь ласка, використовуйте кнопки для вибору мови.",
                        cancellationToken: cancellationToken);
                }
                break;

            case ConversationStep.SelectingService:
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Please use the provided buttons to select a service.",
                    cancellationToken: cancellationToken);
                break;

            default:
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Please use the provided buttons to navigate.",
                    cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message!.Chat.Id;
        var data = callbackQuery.Data!;

        if (!_userStateService.TryGetState(chatId, out var state))
        {
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Please start over with /start",
                cancellationToken: cancellationToken);
            return;
        }

        switch (state.CurrentStep)
        {
            case ConversationStep.SelectingService:
                if (data.StartsWith("service_"))
                    await HandleServiceSelectionAsync(chatId, data, state, cancellationToken);
                break;
            case ConversationStep.SelectingMonth:
                if (data.StartsWith("month_"))
                    await HandleMonthSelectionAsync(chatId, data, state, cancellationToken);
                break;
            case ConversationStep.SelectingDay:
                if (data.StartsWith("day_"))
                    await HandleDaySelectionAsync(chatId, data, state, cancellationToken);
                break;
            case ConversationStep.SelectingTimeSlot:
                if (data.StartsWith("time_"))
                    await HandleTimeSlotSelectionAsync(chatId, data, state, cancellationToken);
                break;
        }
    }

    private async Task SendLanguageSelectionAsync(long chatId, CancellationToken cancellationToken)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("English"), new KeyboardButton("Українська") }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Please select your language / Будь ласка, оберіть мову",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleLanguageSelectionAsync(long chatId, string language, ClientConversationState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting language to {Language} for chatId: {ChatId}", language, chatId);
        
        state.Language = language;
        state.CurrentStep = ConversationStep.SelectingService;
        
        await SendServiceSelectionAsync(chatId, state, cancellationToken);
    }

    private async Task SendServiceSelectionAsync(long chatId, ClientConversationState state, CancellationToken cancellationToken)
    {
        var services = await _dbContext.Services
            .Where(s => s.EmployeeId == state.CompanyId)
            .ToListAsync(cancellationToken);

        var buttons = services.Select(s => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"{s.Name} - {s.Price:C}",
                $"service_{s.Id}")
        }).ToArray();

        var keyboard = new InlineKeyboardMarkup(buttons);

        var text = state.Language == ClientConversationState.EnglishLanguage 
            ? "Please select a service:"
            : "Пожалуйста, выберите услугу:";

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: text,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleServiceSelectionAsync(long chatId, string callbackData, ClientConversationState state, CancellationToken cancellationToken)
    {
        try
        {
            var serviceId = int.Parse(callbackData.Split('_')[1]);
            
            var service = await _dbContext.Services.FindAsync(serviceId);
            if (service == null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: state.Language == ClientConversationState.EnglishLanguage
                        ? "Service not found. Please try again."
                        : "Послугу не знайдено. Будь лас��а, спробуйте ще раз.",
                    cancellationToken: cancellationToken);
                return;
            }

            state.SelectedServiceId = serviceId;
            state.CurrentStep = ConversationStep.SelectingMonth;

            await SendMonthSelectionAsync(chatId, state, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing service selection. CallbackData: {CallbackData}", callbackData);
            var errorText = state.Language == ClientConversationState.EnglishLanguage
                ? "Sorry, there was an error processing your selection. Please try again."
                : "Извините, произошла ошибка при обработке вашего выбора. Пожалуйста, попробуйте снова.";
            
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: errorText,
                cancellationToken: cancellationToken);
        }
    }

    private async Task SendMonthSelectionAsync(long chatId, ClientConversationState state, CancellationToken cancellationToken)
    {
        var currentMonth = DateTime.UtcNow;
        var buttons = new List<IEnumerable<InlineKeyboardButton>>();

        // Show current month and next 2 months
        for (int i = 0; i < 3; i++)
        {
            var month = currentMonth.AddMonths(i);
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    GetMonthName(month, state.Language),
                    $"month_{month:yyyyMM}")
            });
        }

        var keyboard = new InlineKeyboardMarkup(buttons);

        var text = state.Language == ClientConversationState.EnglishLanguage
            ? "Please select a month:"
            : "Будь ласка, оберіть місяць:";

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: text,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleMonthSelectionAsync(long chatId, string callbackData, ClientConversationState state, CancellationToken cancellationToken)
    {
        var monthStr = callbackData.Replace("month_", "");
        var selectedMonth = DateTime.SpecifyKind(
            DateTime.ParseExact(monthStr, "yyyyMM", CultureInfo.InvariantCulture),
            DateTimeKind.Utc);
        
        state.SelectedMonth = selectedMonth;
        state.CurrentStep = ConversationStep.SelectingDay;

        await SendDaySelectionAsync(chatId, state, cancellationToken);
    }

    private async Task SendDaySelectionAsync(long chatId, ClientConversationState state, CancellationToken cancellationToken)
    {
        var service = await _dbContext.Services
            .Include(s => s.Employee)
                .ThenInclude(c => c.WorkingHours)
            .FirstOrDefaultAsync(s => s.Id == state.SelectedServiceId);

        if (service == null) return;

        var buttons = new List<IEnumerable<InlineKeyboardButton>>();
        var currentRow = new List<InlineKeyboardButton>();
        var month = state.SelectedMonth;
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = DateTime.SpecifyKind(
                new DateTime(month.Year, month.Month, day), 
                DateTimeKind.Utc);
            
            // Skip past days
            if (date.Date < DateTime.UtcNow.Date) continue;

            // Skip days where company doesn't work
            var dayOfWeek = date.DayOfWeek;
            if (!service.Employee.WorkingHours.Any(wh => wh.DayOfWeek == dayOfWeek)) continue;

            currentRow.Add(InlineKeyboardButton.WithCallbackData(
                day.ToString(),
                $"day_{date:yyyyMMdd}"));

            if (currentRow.Count == 7 || day == daysInMonth)
            {
                buttons.Add(currentRow.ToArray());
                currentRow = new List<InlineKeyboardButton>();
            }
        }

        var keyboard = new InlineKeyboardMarkup(buttons);

        var text = state.Language == ClientConversationState.EnglishLanguage
            ? "Please select a day:"
            : "Будь ласка, оберіть день:";

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: text,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private  static string GetMonthName(DateTime date, string language)
    {
        return language == ClientConversationState.EnglishLanguage
            ? date.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
            : date.ToString("MMMM yyyy", new CultureInfo("uk-UA"));
    }

    private async Task HandleTimeSlotSelectionAsync(long chatId, string callbackData, ClientConversationState state, CancellationToken cancellationToken)
    {
        try 
        {
            var timeString = callbackData.Replace("time_", "");
            var selectedTime = DateTime.SpecifyKind(
                DateTime.ParseExact(timeString, "yyyyMMddHHmm", CultureInfo.InvariantCulture),
                DateTimeKind.Utc);

            var booking = new Booking
            {
                ServiceId = state.SelectedServiceId,
                BookingTime = selectedTime,
                ClientId = chatId,
                CompanyId = state.CompanyId
            };

            _dbContext.Bookings.Add(booking);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var localTime = TimeZoneInfo.ConvertTimeFromUtc(selectedTime, TimeZoneInfo.Local);
            var confirmationText = state.Language == ClientConversationState.EnglishLanguage
                ? $"Your booking is confirmed for {localTime:g}"
                : $"Ваш запис підтверджено на {localTime:g}";

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: confirmationText,
                cancellationToken: cancellationToken);

            _userStateService.RemoveState(chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing time slot selection. CallbackData: {CallbackData}", callbackData);
            var errorText = state.Language == ClientConversationState.EnglishLanguage
                ? "Sorry, there was an error processing your selection. Please try again."
                : "Вибачте, сталась помилка при обробці вашого запиту. Будь ласка, спробуйте ще раз.";
            
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: errorText,
                cancellationToken: cancellationToken);
        }
    }

    private async Task<IEnumerable<DateTime>> GenerateAvailableTimeSlotsAsync(Service service, CancellationToken cancellationToken)
    {
        var slots = new List<DateTime>();
        var currentDate = DateTime.UtcNow.Date;
        
        // Get existing bookings for this service
        var existingBookings = await _dbContext.Bookings
            .Where(b => b.ServiceId == service.Id && 
                        b.BookingTime >= currentDate && 
                        b.BookingTime <= currentDate.AddDays(7))
            .Select(b => new { b.BookingTime, b.ServiceId })
            .ToListAsync(cancellationToken);

        for (int day = 0; day < 7; day++)
        {
            var date = currentDate.AddDays(day);
            var workingHours = service.Employee.WorkingHours
                .FirstOrDefault(wh => (int)wh.DayOfWeek == (int)date.DayOfWeek);

            if (workingHours != null)
            {
                var startTime = DateTime.SpecifyKind(date.Add(workingHours.StartTime), DateTimeKind.Utc);
                var endTime = DateTime.SpecifyKind(date.Add(workingHours.EndTime), DateTimeKind.Utc);
                var currentSlot = startTime;

                // Generate slots based on service duration
                while (currentSlot.Add(service.Duration) <= endTime)
                {
                    // Check if the entire duration of the service fits within working hours
                    var slotEnd = currentSlot.Add(service.Duration);
                    
                    // Check if this slot overlaps with any existing bookings
                    bool isSlotAvailable = !existingBookings.Any(b => 
                        (b.BookingTime >= currentSlot && b.BookingTime < slotEnd) || // booking starts during our slot
                        (b.BookingTime.Add(service.Duration) > currentSlot && b.BookingTime < slotEnd)); // booking overlaps our slot

                    if (isSlotAvailable)
                    {
                        slots.Add(currentSlot);
                    }

                    // Move to next slot
                    currentSlot = currentSlot.AddMinutes(30); // Standard 30-minute intervals
                }
            }
        }

        return slots.OrderBy(s => s);
    }

    private async Task HandleDaySelectionAsync(long chatId, string callbackData, ClientConversationState state, CancellationToken cancellationToken)
    {
        try
        {
            var dayStr = callbackData.Replace("day_", "");
            var selectedDay = DateTime.ParseExact(dayStr, "yyyyMMdd", CultureInfo.InvariantCulture);
            
            state.SelectedDay = selectedDay;
            state.CurrentStep = ConversationStep.SelectingTimeSlot;

            var service = await _dbContext.Services
                .Include(s => s.Employee)
                    .ThenInclude(c => c.WorkingHours)
                .FirstOrDefaultAsync(s => s.Id == state.SelectedServiceId);

            if (service == null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: state.Language == ClientConversationState.EnglishLanguage 
                        ? "Service not found. Please start over."
                        : "Послугу не знайдено. Будь ласка, почніть спочатку.",
                    cancellationToken: cancellationToken);
                return;
            }

            var availableTimeSlots = await GenerateAvailableTimeSlotsAsync(service, cancellationToken);
            var dayTimeSlots = availableTimeSlots.Where(ts => ts.Date == selectedDay.Date);

            if (!dayTimeSlots.Any())
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: state.Language == ClientConversationState.EnglishLanguage
                        ? "No available time slots for the selected day."
                        : "Немає доступних часових проміжків на обраний день.",
                    cancellationToken: cancellationToken);
                return;
            }

            var buttons = dayTimeSlots.Select(ts => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{ts:HH:mm} - {ts.Add(service.Duration):HH:mm}",
                    $"time_{ts:yyyyMMddHHmm}")
            }).ToArray();

            var keyboard = new InlineKeyboardMarkup(buttons);

            var text = state.Language == ClientConversationState.EnglishLanguage
                ? "Please select a time slot:"
                : "Будь ласка, оберіть час:";

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: text,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing day selection. CallbackData: {CallbackData}", callbackData);
            var errorText = state.Language == ClientConversationState.EnglishLanguage
                ? "Sorry, there was an error processing your selection. Please try again."
                : "Вибачте, сталась помилка при обробці вашого запиту. Будь ласка, спробуйте ще раз.";
            
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: errorText,
                cancellationToken: cancellationToken);
        }
    }
}
