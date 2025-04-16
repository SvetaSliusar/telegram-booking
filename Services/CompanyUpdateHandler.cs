using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text;
using Telegram.Bot.Commands;

namespace Telegram.Bot.Services;

public class CompanyUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<CompanyUpdateHandler> _logger;
    private readonly BookingDbContext _dbContext;
    private readonly ICallbackCommandFactory _commandFactory;
    private readonly IUserStateService _userStateService;
    //private static readonly string StickerId = "CAACAgIAAxkBAAONZnAGyWndlNzCY-3FkmBvV5Y_hskAAi4AAyRxYhqI6DZDakBDFDUE";
    
    // Thread-safe dictionary for conversation states
    private static ConcurrentDictionary<long, string> userConversations = new ConcurrentDictionary<long, string>();
    private static ConcurrentDictionary<long, string> userLanguages = new ConcurrentDictionary<long, string>();
    private readonly ICompanyCreationStateService _companyCreationStateService;

    public CompanyUpdateHandler(
        ITelegramBotClient botClient,
        ILogger<CompanyUpdateHandler> logger,
        BookingDbContext dbContext,
        ICallbackCommandFactory callbackCommandFactory,
        IUserStateService userStateService,
        ICompanyCreationStateService companyCreationStateService)
    {
        _botClient = botClient;
        _logger = logger;
        _dbContext = dbContext;
        _commandFactory = callbackCommandFactory;
        _userStateService = userStateService;
        _companyCreationStateService = companyCreationStateService;
    }

    public async Task StartCompanyFlow(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
        // Add persistent menu button
        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("📋 Menu") }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = false
        };

        var existingToken = await _dbContext.Tokens.FirstOrDefaultAsync(t => t.ChatId == chatId, cancellationToken);

        if (existingToken == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "EnterToken"),
                replyMarkup: new ForceReplyMarkup { Selective = true },
                cancellationToken: cancellationToken);

            userConversations[chatId] = "WaitingForToken";
            return;
        }

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "WelcomeBack"),
            replyMarkup: replyKeyboard,
            cancellationToken: cancellationToken);

        await ShowMainMenu(chatId, cancellationToken);
    }

    public Mode GetMode(long chatId)
    {
        return _dbContext.Tokens.Any(t => t.ChatId == chatId) || (userConversations.ContainsKey(chatId) && userConversations[chatId] == "WaitingForToken") ? Mode.Company : Mode.Client;
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        if (update == null) return;

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
        if (message?.Text is not { } userMessage) return;
        var chatId = message.Chat.Id;
        var language = userLanguages.GetValueOrDefault(chatId, "EN");

        // Handle Menu button click
        if (userMessage == "/menu")
        {
            await ShowMainMenu(chatId, cancellationToken);
            return;
        }
        var state = _userStateService.GetConversation(chatId);
        if (!string.IsNullOrEmpty(state))
        {
            if (state.StartsWith("WaitingForBreakStart_"))
            {
                var parts = state.Split('_');
                var employeeId = int.Parse(parts[1]);
                var day = (DayOfWeek)int.Parse(parts[2]);
                await HandleBreakStartTimeInput(chatId, employeeId, day, userMessage, cancellationToken);
                return;
            }

            if (state.StartsWith("WaitingForBreakEnd_"))
            {
                var parts = state.Split('_');
                var employeeId = int.Parse(parts[1]);
                var day = (DayOfWeek)int.Parse(parts[2]);
                var startTime = TimeSpan.Parse(parts[3]);
                await HandleBreakEndTimeInput(chatId, employeeId, day, startTime, userMessage, cancellationToken);
                return;
            }

            if (state.StartsWith("WaitingForWorkStartTime_"))
            {
                var parts = state.Split('_');
                var employeeId = int.Parse(parts[1]);
                var day = (DayOfWeek)int.Parse(parts[2]);
                await HandleStartWorkTimeInput(chatId, employeeId, day, userMessage, cancellationToken);
                return;
            }

            if (state.StartsWith("WaitingForWorkEndTime_"))
            {
                var parts = state.Split('_');
                var employeeId = int.Parse(parts[1]);
                var day = (DayOfWeek)int.Parse(parts[2]);
                var startTime = TimeSpan.Parse(parts[3]);
                await HandleEndWorkTimeInput(chatId, employeeId, day, startTime, userMessage, cancellationToken);
                return;
            }

            var isSuccess = await HandleCompanyCreationInput(chatId, userMessage, cancellationToken);
            if (isSuccess) return;
        }

        // Handle default message
        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "UseMenuButton"),
            cancellationToken: cancellationToken);
    }

    private async Task HandleBreakStartTimeInput(long chatId, int employeeId, DayOfWeek day, string timeInput, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
        if (!TimeSpan.TryParse(timeInput, out TimeSpan startTime))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }

        // Get working hours for validation
        var workingHours = await _dbContext.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.EmployeeId == employeeId && wh.DayOfWeek == day, cancellationToken);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoWorkingHours"),
                cancellationToken: cancellationToken);
            return;
        }

        // Validate that start time is within working hours
        if (startTime < workingHours.StartTime || startTime >= workingHours.EndTime)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidBreakTime"),
                cancellationToken: cancellationToken);
            return;
        }
        _userStateService.SetConversation(chatId, $"WaitingForBreakEnd_{employeeId}_{(int)day}_{startTime}");

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BreakEndTime"),
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), $"waiting_for_break_start:{employeeId}_{(int)day}") }
            }),
            cancellationToken: cancellationToken);
    }

    private async Task HandleStartWorkTimeInput(long chatId, int employeeId, DayOfWeek day, string timeInput, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
        if (!TimeSpan.TryParse(timeInput, out TimeSpan startTime))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }

        var workingHours = await _dbContext.WorkingHours
            .FirstOrDefaultAsync(wh => wh.EmployeeId == employeeId && wh.DayOfWeek == day, cancellationToken);

        if (startTime < workingHours?.StartTime || startTime >= workingHours?.EndTime)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }
        _userStateService.SetConversation(chatId, $"WaitingForWorkEndTime_{employeeId}_{(int)day}_{startTime}");

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "WorkTimeEnd"),
            replyMarkup: new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Back"), $"change_work_time") }
            }),
            cancellationToken: cancellationToken);
    }

    private async Task HandleEndWorkTimeInput(long chatId, int employeeId, DayOfWeek day, TimeSpan startTime, string timeInput, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
        if (!TimeSpan.TryParse(timeInput, out TimeSpan endTime))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }

        // Get working hours for validation
        var workingHours = await _dbContext.WorkingHours
            .FirstOrDefaultAsync(wh => wh.EmployeeId == employeeId && wh.DayOfWeek == day, cancellationToken);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoWorkingHours"),
                cancellationToken: cancellationToken);
            return;
        }

        if (endTime <= startTime)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidWorkTime"),
                cancellationToken: cancellationToken);
            return;
        }

        workingHours.StartTime = startTime;
        workingHours.EndTime = endTime;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Clear the conversation state
        _userStateService.RemoveConversation(chatId);

        await _botClient.SendMessage(
            chatId: chatId,
            text: "✅ Work time is updated.",
            cancellationToken: cancellationToken);
    }

    private async Task HandleBreakEndTimeInput(long chatId, int employeeId, DayOfWeek day, TimeSpan startTime, string timeInput, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
        if (!TimeSpan.TryParse(timeInput, out TimeSpan endTime))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidTimeFormat"),
                cancellationToken: cancellationToken);
            return;
        }

        // Get working hours for validation
        var workingHours = await _dbContext.WorkingHours
            .Include(wh => wh.Breaks)
            .FirstOrDefaultAsync(wh => wh.EmployeeId == employeeId && wh.DayOfWeek == day, cancellationToken);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoWorkingHours"),
                cancellationToken: cancellationToken);
            return;
        }

        // Validate that end time is within working hours and after start time
        if (endTime <= startTime || endTime > workingHours.EndTime)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidBreakTime"),
                cancellationToken: cancellationToken);
            return;
        }

        // Check for overlapping breaks
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

        // Clear the conversation state
        _userStateService.RemoveConversation(chatId);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BreakAdded"),
            cancellationToken: cancellationToken);

        // Return to day breaks selection
        await HandleDayBreaksSelection(chatId, employeeId, day, cancellationToken);
    }

    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrEmpty(callbackQuery.Data))
            return;

        // First, try new factory-based commands
        var command = _commandFactory.CreateCommand(callbackQuery);
        if (command != null)
        {
            await command.ExecuteAsync(callbackQuery, cancellationToken);
            return;
        }

        var chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data;

        try
        {
            var language = userLanguages.GetValueOrDefault(chatId, "EN");

            // Add new handlers for service management
            switch (data)
            {
                case "back_to_menu":
                    await ShowMainMenu(chatId, cancellationToken);
                    return;
            }

            if (data.StartsWith("select_employee:"))
            {
                await HandleEmployeeSelectionForService(callbackQuery, cancellationToken);
                return;
            }
            if (data.StartsWith("reminder_time"))
            {
                await HandleReminderTimeSelection(callbackQuery, cancellationToken);
                return;
            }

            // ✅ Ensure data is in the expected format
            if (string.IsNullOrEmpty(data)) return;

            // ✅ Handle other actions
            switch (data)
            {
                case "create_company":
                    await StartCompanyCreation(chatId, cancellationToken);
                    break;

                case "view_daily_bookings":
                    userConversations[chatId] = "WaitingForBookingDate";
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
                var selectedDate = DateTime.Parse(data.Replace("booking_date_", ""));
                await HandleBookingDateSelection(chatId, selectedDate, cancellationToken);
            }
            else if (data.StartsWith("booking_prev_") || data.StartsWith("booking_next_"))
            {
                var referenceDate = DateTime.Parse(data.Replace("booking_prev_", "").Replace("booking_next_", ""));
                DateTime newMonth = data.StartsWith("booking_prev_") 
                    ? referenceDate.AddMonths(-1) 
                    : referenceDate.AddMonths(1);
                await ShowBookingCalendar(chatId, newMonth, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing callback query: {CallbackData}", callbackQuery.Data);
            await _botClient.SendTextMessageAsync(chatId, "An error occurred while processing the request. Please try again later.", cancellationToken: cancellationToken);
        }
    }

    // ✅ Step 1: Prompt User for Company Name
    private async Task StartCompanyCreation(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");

        _userStateService.SetConversation(chatId, "WaitingForCompanyName");

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "EnterBusinessName"),
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private static ConcurrentDictionary<long, CompanyCreationData> userInputs = new ConcurrentDictionary<long, CompanyCreationData>();
    // ✅ Step 2: Handle User's Input for Company Details
    private async Task<bool> HandleCompanyCreationInput(long chatId, string userMessage, CancellationToken cancellationToken)
    {
        var userState = _userStateService.GetConversation(chatId);
        if (string.IsNullOrEmpty(userState)) return false;
        var language = _userStateService.GetLanguage(chatId);
        var creationState = _companyCreationStateService.GetState(chatId);

        switch (userState)
        {
            case "WaitingForCompanyName":
                _companyCreationStateService.SetCompanyName(chatId, userMessage);
                _companyCreationStateService.SetCompanyAlias(chatId, GenerateCompanyAlias(userMessage));
                _userStateService.SetConversation(chatId, "WaitingForEmployeeName");

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "EnterYourName"),
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                break;

            case "WaitingForEmployeeName":
                _companyCreationStateService.AddEmployee(chatId, new EmployeeCreationData
                {
                    Name = userMessage,
                    Services = new List<int>(),
                    WorkingDays = new List<DayOfWeek>(),
                    WorkingHours = new List<WorkingHoursData>()
                });

                await SaveCompanyData(chatId, cancellationToken);
                _companyCreationStateService.ClearState(chatId);

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "BusinessCreated"),
                    cancellationToken: cancellationToken);

                await ShowMainMenu(chatId, cancellationToken);
                break;
            
            case "WaitingForServiceName":
                var service = creationState.Services.FirstOrDefault(s => s.Id == creationState.CurrentServiceIndex);
                if (service == null)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Error: Session expired. Please try again from the main menu.",
                        cancellationToken: cancellationToken);
                    return false;
                }

                service.Name = userMessage;
                _companyCreationStateService.UpdateService(chatId, service);
                _userStateService.SetConversation(chatId, "WaitingFoServiceDescription");
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "EnterServiceDescription"),
                    cancellationToken: cancellationToken);
                break;
            case "WaitingFoServiceDescription":
                service = creationState.Services.FirstOrDefault(s => s.Id == creationState.CurrentServiceIndex);
                if (service == null)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Error: Session expired. Please try again from the main menu.",
                        cancellationToken: cancellationToken);
                    return false;
                }

                service.Description = userMessage;
                _companyCreationStateService.UpdateService(chatId, service);
                _userStateService.SetConversation(chatId, "WaitingForServicePrice");
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "EnterServicePrice"),
                    cancellationToken: cancellationToken);
                break;

            case "WaitingForServicePrice":
                await HandleServicePriceInput(chatId, userMessage, cancellationToken);
                break;

            case "WaitingForCustomDuration":
                if (!int.TryParse(userMessage, out var customDuration) || customDuration <= 0)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Please enter a valid duration in minutes (e.g., 20):",
                        cancellationToken: cancellationToken);
                    return false;
                }

                service = _companyCreationStateService.GetState(chatId).Services.LastOrDefault();
                if (service == null)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Error: Session expired. Please try again from the main menu.",
                        cancellationToken: cancellationToken);
                    return false;
                }

                service.Duration = customDuration;
                _companyCreationStateService.UpdateService(chatId, service);
                await SaveNewService(chatId, cancellationToken);
                break;

            default:
                return false;
        }

        return true;
    }

    private static string GenerateCompanyAlias(string companyName)
    {
        // Convert to lowercase and replace spaces with underscores
        var alias = companyName.ToLower()
            .Replace(" ", "_")
            .Replace("-", "_")
            .Replace(".", "_")
            .Replace(",", "_")
            .Replace("'", "")
            .Replace("\"", "");

        // Remove any non-alphanumeric characters except underscores
        alias = new string(alias.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        // Remove consecutive underscores
        alias = alias.Replace("__", "_");

        // Trim underscores from start and end
        alias = alias.Trim('_');

        return alias;
    }

    // ✅ Step 3: Save Company to Database
   private async Task SaveCompanyData(long chatId, CancellationToken cancellationToken)
    {
        var state = _companyCreationStateService.GetState(chatId);
        var company = new Company
        {
            Name = state.CompanyName,
            Alias = state.CompanyAlias,
            TokenId = (await _dbContext.Tokens.FirstAsync(t => t.ChatId == chatId)).Id,
            Employees = state.Employees.Select(e => new Employee
            {
                Name = e.Name
            }).ToList()
        };

        _dbContext.Companies.Add(company);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ShowMainMenu(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var company = await _dbContext.Companies.FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        var keyboardButtons = company == null
            ? new List<List<InlineKeyboardButton>>
            {
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "CreateCompany"), "create_company") }
            }
            : new List<List<InlineKeyboardButton>>
            {
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "SetupWorkDays"), "setup_work_days") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ChangeWorkTime"), "change_work_time") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ManageBreaks"), "manage_breaks") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ListServices"), "list_services") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "AddService"), "add_service") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "GetClientLink"), "get_client_link") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ViewDailyBookings"), "view_daily_bookings") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ReminderSettings"), "reminder_settings") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ChangeLanguage"), "change_language") }
            };

        var keyboard = new InlineKeyboardMarkup(keyboardButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "MainMenu"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleEmployeeSelectionForService(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        var employeeId = int.Parse(callbackQuery.Data.Split(':')[1]);

        var company = await _dbContext.Companies
            .Include(c => c.Employees)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var employee = company?.Employees.FirstOrDefault(e => e.Id == employeeId);
        if (employee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text:  Translations.GetMessage(language, "NoEmployeeFound"),
                cancellationToken: cancellationToken);
            return;
        }

        userInputs[chatId] = new CompanyCreationData
        {
            CompanyName = company.Name,
            SelectedEmployeeId = employeeId,
            Services = new List<ServiceCreationData>()
        };

        userConversations[chatId] = "WaitingForServiceName";

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "NewService", employee.Name),
            cancellationToken: cancellationToken);
    }

    private async Task HandleServicePriceInput(long chatId, string priceInput, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetConversation(chatId);
        
        if (!decimal.TryParse(priceInput, out decimal price) || price < 0)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidPrice"),
                cancellationToken: cancellationToken);
            return;
        }

        var state = _companyCreationStateService.GetState(chatId);
        var service = state.Services.FirstOrDefault(s => s.Id == state.CurrentServiceIndex);
        if (service == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: Session expired. Please try again from the main menu.",
                cancellationToken: cancellationToken);
            return;
        }
        
        service.Price = price;
        userConversations[chatId] = "WaitingForServiceDuration";

        var predefinedDurations = new InlineKeyboardMarkup(new[]
        {
            new [] { InlineKeyboardButton.WithCallbackData("10 min", "service_duration:10"), InlineKeyboardButton.WithCallbackData("15 min", "service_duration:15") },
            new [] { InlineKeyboardButton.WithCallbackData("30 min", "service_duration:30"), InlineKeyboardButton.WithCallbackData("45 min", "service_duration:45") },
            new [] { InlineKeyboardButton.WithCallbackData("Custom", "service_duration:custom") }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "ChooseDuration"),
            replyMarkup: predefinedDurations,
            cancellationToken: cancellationToken);
    }

    private async Task SaveNewService(long chatId, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var creationData = _companyCreationStateService.GetState(chatId);
        if (creationData == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "SessionExpired"),
                cancellationToken: cancellationToken);
            return;
        }

        // Handle adding service to existing employee
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
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

        if (employee.Services == null)
        {
            employee.Services = new List<Service>();
        }
        var serviceCreationData = creationData.Services.FirstOrDefault(s => s.Id == creationData.CurrentServiceIndex);

        if (serviceCreationData == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "SessionExpired"),
                cancellationToken: cancellationToken);
            return;
        }

        var service = new Service
        {
            Name = serviceCreationData.Name,
            Price = serviceCreationData.Price,
            Duration = TimeSpan.FromMinutes(serviceCreationData.Duration),
            Description = "Service description",
            EmployeeId = employee.Id
        };

        employee.Services.Add(service);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "ServiceAddedForEmployee", service.Name, employee.Name),
            cancellationToken: cancellationToken);
        
        _companyCreationStateService.RemoveService(chatId, serviceCreationData.Id);
        _userStateService.RemoveConversation(chatId);

        await ShowMainMenu(chatId, cancellationToken);
    }

    private async Task ShowBookingCalendar(long chatId, DateTime selectedDate, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
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
        
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        // Get all bookings for this company in the current month
        var monthStart = DateTime.SpecifyKind(new DateTime(selectedDate.Year, selectedDate.Month, 1), DateTimeKind.Utc);
        var monthEnd = DateTime.SpecifyKind(monthStart.AddMonths(1).AddDays(-1), DateTimeKind.Utc);
        
        var bookedDates = await _dbContext.Bookings
            .Where(b => b.CompanyId == company.Id && 
                        b.BookingTime >= monthStart && 
                        b.BookingTime <= monthEnd)
            .Select(b => b.BookingTime.Date)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Generate day buttons
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = DateTime.SpecifyKind(new DateTime(selectedDate.Year, selectedDate.Month, day), DateTimeKind.Utc);
            var hasBookings = bookedDates.Contains(date.Date);
            var isPastDate = date.Date < currentDate;
            
            if (isPastDate)
            {
                weekRow.Add(InlineKeyboardButton.WithCallbackData($"{day}⚫", "ignore"));
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
            ? InlineKeyboardButton.WithCallbackData("⬅️", "ignore")
            : InlineKeyboardButton.WithCallbackData("⬅️", $"booking_prev_{selectedDate:yyyy-MM-dd}");

        var nextButton = isFutureMonth || isNextMonth
            ? InlineKeyboardButton.WithCallbackData("➡️", "ignore")
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
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

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
                    new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), "back_to_menu") }
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
                booking.Client.Name,
                localTime.ToString("hh:mm tt"),
                booking.Client.Name ?? "N/A") + "\n\n";
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

    private async Task HandleReminderSettings(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
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
                HoursBeforeReminder = 24
            };
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("1 hour", "reminder_time:1") },
            new[] { InlineKeyboardButton.WithCallbackData("3 hours", "reminder_time:3") },
            new[] { InlineKeyboardButton.WithCallbackData("6 hours", "reminder_time:6") },
            new[] { InlineKeyboardButton.WithCallbackData("12 hours", "reminder_time:12") },
            new[] { InlineKeyboardButton.WithCallbackData("24 hours", "reminder_time:24") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), "back_to_menu") }
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
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        string data = callbackQuery.Data;

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
                HoursBeforeReminder = hours
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

        await ShowMainMenu(chatId, cancellationToken);
    }

    private async Task HandleDayBreaksSelection(long chatId, int employeeId, DayOfWeek day, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
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
