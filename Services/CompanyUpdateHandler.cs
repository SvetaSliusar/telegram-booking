using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Exceptions;
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

    public async Task ShowRoleSelection(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
        InlineKeyboardMarkup inlineKeyboard = new(
            new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "IAmCompany"), "choose_company"),
                    InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "IAmClient"), "choose_client")
                }
            });

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "Welcome"),
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken);
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

        await HandleCompanyCreationInput(chatId, userMessage, cancellationToken);
        return;
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

            if (data.StartsWith("copy_hours:"))
            {
                await HandleCopyHours(callbackQuery, cancellationToken);
                return;
            }

            // Add new handlers for service management
            switch (data)
            {
                case "list_services":
                    await ListServices(chatId, cancellationToken);
                    return;

                case "add_service":
                    await StartAddService(chatId, cancellationToken);
                    return;

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

            if (data.StartsWith("service_duration:"))
            {
                var durationValue = data.Split(':')[1];
                var userData = userInputs[chatId];
                
                if (userData == null || userData.Services == null || !userData.Services.Any())
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Error: Session expired. Please try again from the main menu.",
                        cancellationToken: cancellationToken);
                    return;
                }

                if (durationValue == "custom")
                {
                    userConversations[chatId] = "WaitingForCustomDuration";

                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "⏳ Enter the custom duration in minutes (e.g., 20):",
                        cancellationToken: cancellationToken);
                    return;
                }

                if (int.TryParse(durationValue, out int duration))
                {
                    userData.Services[0].Duration = duration;
                    await SaveNewService(chatId, cancellationToken);
                }
                return;
            }

            // ✅ Ensure data is in the expected format
            if (string.IsNullOrEmpty(data)) return;

            // ✅ Handle workday selection for setting hours
            if (data.StartsWith("select_day_for_hours:"))
            {
                string selectedDay = data.Split(':')[1]; // Extract day from "select_day_for_hours:Monday"

                if (!Enum.TryParse(selectedDay, true, out DayOfWeek dayOfWeek))
                {
                    await _botClient.SendMessage(chatId, Translations.GetMessage(language, "InvalidDaySelected"), cancellationToken: cancellationToken);
                    return;
                }

                // ✅ Ask for working hours for this specific day
                await SendReplyWithWorkingHours(chatId, dayOfWeek, cancellationToken);
                return;
            }

            // ✅ Fix parsing for `workinghours:<DayOfWeek>:<Time>`
            if (data.StartsWith("workinghours:"))
            {
                string[] splitData = data.Split(':', 3);

                if (splitData.Length < 3) return;

                if (!Enum.TryParse(splitData[1], true, out DayOfWeek selectedDay)) return;
                string selectedHour = splitData[2];

                if (!userHoursSelections.ContainsKey(chatId))
                    userHoursSelections[chatId] = new Dictionary<DayOfWeek, List<TimeSpan>>();

                if (!userHoursSelections[chatId].ContainsKey(selectedDay))
                    userHoursSelections[chatId][selectedDay] = new List<TimeSpan>();

                var selectedList = userHoursSelections[chatId][selectedDay];

                // ✅ If user selects a section (Morning, Afternoon, Evening)
                if (selectedHour == "Morning")
                {
                    ToggleSectionSelection(selectedList, morningWorkingHours);
                }
                else if (selectedHour == "Afternoon")
                {
                    ToggleSectionSelection(selectedList, afternoonWorkingHours);
                }
                else if (selectedHour == "Evening")
                {
                    ToggleSectionSelection(selectedList, eveningWorkingHours);
                }
                else
                {
                    // ✅ Toggle individual hour selection
                    TimeSpan timeSpan = TimeSpan.Parse(selectedHour);
                    if (selectedList.Contains(timeSpan))
                        selectedList.Remove(timeSpan);
                    else
                        selectedList.Add(timeSpan);
                }

                // ✅ Refresh UI for the selected day
                await SendReplyWithWorkingHours(chatId, selectedDay, cancellationToken);
                return;
            }

            // ✅ Handle confirmation of selected hours
            if (data.StartsWith("confirm_hours:"))
            {
                if (!Enum.TryParse(data.Split(':')[1], true, out DayOfWeek selectedDay)) return;
                await SaveWorkingHours(chatId, selectedDay, cancellationToken);
                return;
            }

            // ✅ Handle clearing selection
            if (data.StartsWith("clear_hours:"))
            {
                if (!Enum.TryParse(data.Split(':')[1], true, out DayOfWeek selectedDay)) return;
                userHoursSelections[chatId][selectedDay].Clear();
                await SendReplyWithWorkingHours(chatId, selectedDay, cancellationToken);
                return;
            }

            // ✅ Fix parsing for `workingdays` callbacks
            if (data.StartsWith("workingdays:"))
            {
                string selectedDay = data.Split(':')[1];

                if (!userDaysSelections.ContainsKey(chatId))
                    userDaysSelections[chatId] = new List<string>();

                switch (selectedDay)
                {
                    case "Confirm":
                        await SaveWorkingDays(chatId, cancellationToken);
                        return;

                    case "ClearSelection":
                        userDaysSelections[chatId].Clear();
                        break;

                    default:
                        if (userDaysSelections[chatId].Contains(selectedDay))
                        {
                            userDaysSelections[chatId].Remove(selectedDay);
                        }
                        else
                        {
                            userDaysSelections[chatId].Add(selectedDay);
                        }
                        break;
                }

                // ✅ Refresh the UI after selection
                await SendReplyWithWorkingDays(chatId, cancellationToken);
                return;
            }

            // ✅ Handle other actions
            switch (data)
            {
                case "create_company":
                    await StartCompanyCreation(chatId, cancellationToken);
                    break;

                case "setup_work_days":
                    await SendReplyWithWorkingDays(chatId, cancellationToken);
                    break;

                case "setup_work_time":
                    await SendDaySelectionForHours(chatId, cancellationToken); // Ask for day before hours
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

    private async Task HandleHourSelection(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        string[] splitData = callbackQuery.Data.Split(':');

        if (splitData.Length < 3) return;

        if (!Enum.TryParse(splitData[1], true, out DayOfWeek selectedDay))
            return;

        string selectedHour = splitData[2];
        TimeSpan timeSpan = TimeSpan.Parse(selectedHour);

        if (!userHoursSelections.ContainsKey(chatId))
            userHoursSelections[chatId] = new Dictionary<DayOfWeek, List<TimeSpan>>();

        if (!userHoursSelections[chatId].ContainsKey(selectedDay))
            userHoursSelections[chatId][selectedDay] = new List<TimeSpan>();

        // ✅ Toggle selection
        if (userHoursSelections[chatId][selectedDay].Contains(timeSpan))
            userHoursSelections[chatId][selectedDay].Remove(timeSpan);
        else
            userHoursSelections[chatId][selectedDay].Add(timeSpan);

        await SendReplyWithWorkingHours(chatId, selectedDay, cancellationToken);
    }


    // ✅ Step 1: Prompt User for Company Name
    private async Task StartCompanyCreation(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
        // Initialize userInputs for this chat ID
        if (!userInputs.ContainsKey(chatId))
        {
            userInputs[chatId] = new CompanyCreationData
            {
                Employees = new List<EmployeeCreationData>(),
                CurrentEmployeeIndex = 0
            };
        }

        userConversations[chatId] = "WaitingForCompanyName";

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "EnterBusinessName"),
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private static ConcurrentDictionary<long, CompanyCreationData> userInputs = new ConcurrentDictionary<long, CompanyCreationData>();
    // ✅ Step 2: Handle User's Input for Company Details
    private async Task HandleCompanyCreationInput(long chatId, string userMessage, CancellationToken cancellationToken)
    {
        var state = _userStateService.GetConversation(chatId);
        if (string.IsNullOrEmpty(state)) return;
        var language = _userStateService.GetLanguage(chatId);
        var creationData = _companyCreationStateService.GetState(chatId);
        var service = creationData.Services.FirstOrDefault(s => s.Id == creationData.CurrentServiceIndex);

        switch (state)
        {
            case "WaitingForCompanyName":
                creationData.CompanyName = userMessage;
                // Generate alias from company name
                creationData.CompanyAlias = GenerateCompanyAlias(userMessage);
                creationData.EmployeeCount = 1; // Set to 1 employee
                creationData.Employees = new List<EmployeeCreationData>();
                creationData.CurrentEmployeeIndex = 0;
                userConversations[chatId] = "WaitingForEmployeeName";
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "EnterYourName"),
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                break;

            case "WaitingForEmployeeName":
                // Ensure the Employees list is initialized
                if (creationData.Employees == null)
                    creationData.Employees = new List<EmployeeCreationData>();

                // Add the new employee (business owner)
                creationData.Employees.Add(new EmployeeCreationData
                {
                    Name = userMessage
                });

                // Finalize company creation
                userConversations.TryRemove(chatId, out _);
                await SaveCompanyData(chatId, creationData, cancellationToken);
                userInputs.TryRemove(chatId, out _);

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "BusinessCreated"),
                    cancellationToken: cancellationToken);

                await ShowMainMenu(chatId, cancellationToken);
                break;

            case "WaitingForServiceName":
                if (service == null)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Error: Session expired. Please try again from the main menu.",
                        cancellationToken: cancellationToken);
                    return;
                }

                service.Name = userMessage;
                _userStateService.SetConversation(chatId, "WaitingFoServiceDescription");
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "EnterServiceDescription"),
                    cancellationToken: cancellationToken);
                break;
            case "WaitingFoServiceDescription":
                if (service == null)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Error: Session expired. Please try again from the main menu.",
                        cancellationToken: cancellationToken);
                    return;
                }

                service.Description = userMessage;
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
                    return;
                }

                if (service == null)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Error: Session expired. Please try again from the main menu.",
                        cancellationToken: cancellationToken);
                    return;
                }

                service.Duration = customDuration;
                await SaveNewService(chatId, cancellationToken);
                break;

            default:
                break;
        }
    }

    private string GenerateCompanyAlias(string companyName)
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

    // Dictionary to store user-selected working days
    private static ConcurrentDictionary<long, List<string>> userDaysSelections = new ConcurrentDictionary<long, List<string>>();
    private static ConcurrentDictionary<long, int> lastSentMessageIds = new ConcurrentDictionary<long, int>();

   private async Task<Message> SendReplyWithWorkingDays(long chatId, CancellationToken cancellationToken)
{
    var language = userLanguages.GetValueOrDefault(chatId, "EN");
    // Get existing company and employee
    var company = await _dbContext.Companies
        .Include(c => c.Employees)
            .ThenInclude(e => e.WorkingHours)
        .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

    var employee = company?.Employees.FirstOrDefault();
    var existingDays = employee?.WorkingHours?.Select(wh => wh.DayOfWeek.ToString()).Distinct().ToList() ?? new List<string>();

    if (!userDaysSelections.ContainsKey(chatId))
    {
        userDaysSelections[chatId] = existingDays;
    }

    var selectedDays = userDaysSelections[chatId];

    var messageBuilder = new System.Text.StringBuilder();
    messageBuilder.AppendLine(Translations.GetMessage(language, "CurrentWorkingDays"));
    messageBuilder.AppendLine(selectedDays.Any() 
        ? string.Join(", ", selectedDays)
        : Translations.GetMessage(language, "NoDaysSelected"));

    InlineKeyboardMarkup inlineKeyboardMarkup = new InlineKeyboardMarkup(new[]
    {
        CreateDayRow(new[] { "Monday", "Tuesday" }, selectedDays, chatId),
        CreateDayRow(new[] { "Wednesday", "Thursday" }, selectedDays, chatId),
        CreateDayRow(new[] { "Friday", "Saturday" }, selectedDays, chatId),
        CreateDayRow(new[] { "Sunday" }, selectedDays, chatId),
        new []
        {
            InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "Confirm"), "workingdays:Confirm"),
            InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ClearSelection"), "workingdays:ClearSelection")
        }
    });

    // Delete previous message before sending a new one
    await DeletePreviousMessage(chatId, cancellationToken);

    var sentMessage = await _botClient.SendMessage(
        chatId: chatId,
        text: messageBuilder.ToString(),
        replyMarkup: inlineKeyboardMarkup,
        cancellationToken: cancellationToken);

    // Store the message ID to delete later
    lastSentMessageIds[chatId] = sentMessage.MessageId;

    return sentMessage;
}


    // Helper function to create rows of buttons dynamically
    private static InlineKeyboardButton[] CreateDayRow(string[] days, List<string> selectedDays, long chatId)
    {
        return days.Select(day =>
        {
            bool isSelected = selectedDays.Contains(day);
            string buttonText = isSelected ? $"{day} ✅" : day;
            return InlineKeyboardButton.WithCallbackData(buttonText, $"workingdays:{day}");
        }).ToArray();
    }

    private async Task SaveWorkingHours(long chatId, DayOfWeek selectedDay, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
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

        if (!userHoursSelections.ContainsKey(chatId) || !userHoursSelections[chatId].ContainsKey(selectedDay) ||
            userHoursSelections[chatId][selectedDay].Count == 0)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"❌ No time slots selected for {selectedDay}. Please select at least one.",
                cancellationToken: cancellationToken);
            return;
        }

        // Get the current employee
        var currentEmployee = company.Employees.FirstOrDefault();
        if (currentEmployee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoEmployeeFound"),
                cancellationToken: cancellationToken);
        }
    }

    // ✅ Step 3: Save Company to Database
   private async Task SaveCompanyData(long chatId, CompanyCreationData userData, CancellationToken cancellationToken)
    {
        var company = new Company
        {
            Name = userData.CompanyName,
            Alias = userData.CompanyAlias,
            TokenId = _dbContext.Tokens.First(t => t.ChatId == chatId).Id,
            Employees = userData.Employees.Select(e => new Employee
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
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "SetupWorkTime"), "setup_work_time") },
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

    private async Task SaveWorkingDays(long chatId, CancellationToken cancellationToken)
    {
        var selectedDays = userDaysSelections.GetValueOrDefault(chatId, new List<string>());
        
        if (!selectedDays.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ No days selected. Please select at least one.",
                cancellationToken: cancellationToken);
            return;
        }

        // Convert selected days to DayOfWeek enum
        var workingDays = selectedDays
            .Select(day => Enum.TryParse(day.Trim(), true, out DayOfWeek dayOfWeek) ? dayOfWeek : (DayOfWeek?)null)
            .Where(day => day.HasValue)
            .Select(day => day.Value)
            .ToList();

        // Check if we're in company creation flow
        if (userConversations.TryGetValue(chatId, out var state) && state == "WaitingForEmployeeWorkingDays")
        {
            var userData = userInputs[chatId];
            if (userData == null || userData.Employees == null || userData.CurrentEmployeeIndex >= userData.Employees.Count)
            {
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "❌ Error: Session expired. Please start over.",
                    cancellationToken: cancellationToken);
                return;
            }

            var currentEmployee = userData.Employees[userData.CurrentEmployeeIndex];
            currentEmployee.WorkingDays = workingDays;
            userConversations[chatId] = "WaitingForEmployeeWorkingHours";
            await SendReplyWithWorkingHours(chatId, workingDays.First(), cancellationToken);
            return;
        }

        // Handle updating existing company
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
                .ThenInclude(e => e.WorkingHours)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: No company found. Please start over.",
                cancellationToken: cancellationToken);
            return;
        }

        var employee = company.Employees.FirstOrDefault();
        if (employee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: No employee found for your company.",
                cancellationToken: cancellationToken);
            return;
        }

        // Remove existing working hours for days that are no longer selected
        var existingHours = employee.WorkingHours?.ToList() ?? new List<WorkingHours>();
        var daysToRemove = existingHours
            .Where(wh => !workingDays.Contains(wh.DayOfWeek))
            .ToList();

        foreach (var hour in daysToRemove)
        {
            employee.WorkingHours.Remove(hour);
        }

        // Add new working hours for newly selected days
        foreach (var day in workingDays)
        {
            if (!existingHours.Any(wh => wh.DayOfWeek == day))
            {
                employee.WorkingHours.Add(new WorkingHours
                {
                    DayOfWeek = day,
                    StartTime = TimeSpan.FromHours(9), // Default start time
                    EndTime = TimeSpan.FromHours(17)   // Default end time
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(
            chatId: chatId,
            text: "✅ Working days have been updated successfully!",
            cancellationToken: cancellationToken);

        await ShowMainMenu(chatId, cancellationToken);
    }

    public static string GenerateClientToken(int length = 32)
    {
        const string validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        char[] token = new char[length];

        using (var rng = RandomNumberGenerator.Create())
        {
            byte[] data = new byte[length];
            rng.GetBytes(data);

            for (int i = 0; i < token.Length; i++)
            {
                token[i] = validChars[data[i] % validChars.Length];
            }
        }

        return new string(token);
    }

    private static ConcurrentDictionary<long, Dictionary<DayOfWeek, List<TimeSpan>>> userHoursSelections =
        new ConcurrentDictionary<long, Dictionary<DayOfWeek, List<TimeSpan>>>();

    private async Task SendDaySelectionForHours(long chatId, CancellationToken cancellationToken)
    {
        // Get existing company and employee
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
                .ThenInclude(e => e.WorkingHours)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        var employee = company?.Employees.FirstOrDefault();
        var existingDays = employee?.WorkingHours?.Select(wh => wh.DayOfWeek).Distinct().ToList() ?? new List<DayOfWeek>();

        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine("📅 Current Working Days:");
        if (existingDays.Any())
        {
            messageBuilder.AppendLine(string.Join(", ", existingDays));
        }
        else
        {
            messageBuilder.AppendLine("No days selected");
        }

        InlineKeyboardMarkup inlineKeyboardMarkup = new InlineKeyboardMarkup(new[]
        {
            CreateDayRow(new[] { "Monday", "Tuesday" }, chatId, "select_day_for_hours"),
            CreateDayRow(new[] { "Wednesday", "Thursday" }, chatId, "select_day_for_hours"),
            CreateDayRow(new[] { "Friday", "Saturday", "Sunday" }, chatId, "select_day_for_hours"),
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: messageBuilder.ToString(),
            replyMarkup: inlineKeyboardMarkup,
            cancellationToken: cancellationToken);
    }

    private async Task HandleDaySelectionForHours(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data.Split(':')[1]; // Extract day from "select_day_for_hours:Monday"

        if (!Enum.TryParse(data, true, out DayOfWeek selectedDay))
        {
            await _botClient.SendMessage(chatId, "❌ Invalid day selected.", cancellationToken: cancellationToken);
            return;
        }

        if (!userHoursSelections.ContainsKey(chatId))
            userHoursSelections[chatId] = new Dictionary<DayOfWeek, List<TimeSpan>>();

        // Initialize the day if it doesn't exist
        if (!userHoursSelections[chatId].ContainsKey(selectedDay))
            userHoursSelections[chatId][selectedDay] = new List<TimeSpan>();

        // ✅ Ask for hours for the selected day
        await SendReplyWithWorkingHours(chatId, selectedDay, cancellationToken);
    }


    // ✅ Helper method to generate buttons dynamically
    private static InlineKeyboardButton[] CreateDayRow(string[] days, long chatId, string prefix)
    {
        return days.Select(day =>
            InlineKeyboardButton.WithCallbackData(day, $"{prefix}:{day}")
        ).ToArray();
    }

    private static readonly string[] morningWorkingHours = { "8:00", "8:30", "9:00", "9:30", "10:00", "11:00" };
    private static readonly string[] afternoonWorkingHours = { "12:00", "12:30", "13:00", "13:30", "14:00" };
    private static readonly string[] eveningWorkingHours = { "19:00", "19:30", "20:00", "20:30", "21:00"};

    private async Task<Message> SendReplyWithWorkingHours(long chatId, DayOfWeek selectedDay, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        // Get existing company and employee
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
                .ThenInclude(e => e.WorkingHours)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        var employee = company?.Employees.FirstOrDefault();
        var existingHours = employee?.WorkingHours
            ?.Where(wh => wh.DayOfWeek == selectedDay)
            ?.ToList() ?? new List<WorkingHours>();

        if (!userHoursSelections.ContainsKey(chatId))
        {
            userHoursSelections[chatId] = new Dictionary<DayOfWeek, List<TimeSpan>>();
        }

        if (!userHoursSelections[chatId].ContainsKey(selectedDay))
        {
            userHoursSelections[chatId][selectedDay] = existingHours
                .SelectMany(h => new[] { h.StartTime, h.EndTime })
                .ToList();
        }

        var selectedHours = userHoursSelections[chatId][selectedDay];

        bool morningSelected = morningWorkingHours.All(h => selectedHours.Contains(TimeSpan.Parse(h)));
        bool afternoonSelected = afternoonWorkingHours.All(h => selectedHours.Contains(TimeSpan.Parse(h)));
        bool eveningSelected = eveningWorkingHours.All(h => selectedHours.Contains(TimeSpan.Parse(h)));

        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine(Translations.GetMessage(language, "CurrentWorkingHours", selectedDay));
        if (selectedHours.Any())
        {
            var sortedHours = selectedHours.OrderBy(h => h).ToList();
            var intervals = new List<string>();
            for (int i = 0; i < sortedHours.Count; i += 2)
            {
                if (i + 1 < sortedHours.Count)
                {
                    intervals.Add($"{sortedHours[i]:hh\\:mm}-{sortedHours[i + 1]:hh\\:mm}");
                }
            }
            messageBuilder.AppendLine(Translations.GetMessage(language, "WorkingHoursIntervals", string.Join(", ", intervals)));
        }
        else
        {
            messageBuilder.AppendLine(Translations.GetMessage(language, "NoHoursSelected"));
        }

        // Get all selected working days
        var selectedDays = userDaysSelections.GetValueOrDefault(chatId, new List<string>());
        var otherDays = selectedDays
            .Where(d => d != selectedDay.ToString())
            .Select(d => Enum.TryParse(d, true, out DayOfWeek day) ? day : (DayOfWeek?)null)
            .Where(d => d.HasValue)
            .Select(d => d.Value)
            .ToList();

        var keyboardButtons = new List<List<InlineKeyboardButton>>
        {
            new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData(morningSelected ? "🌅 Morning ✅" : "🌅 Morning", $"workinghours:{selectedDay}:Morning") },
            CreateHourRow(morningWorkingHours, selectedHours, chatId, selectedDay).ToList(),

            new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData(afternoonSelected ? "🌞 Afternoon ✅" : "🌞 Afternoon", $"workinghours:{selectedDay}:Afternoon") },
            CreateHourRow(afternoonWorkingHours, selectedHours, chatId, selectedDay).ToList(),

            new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData(eveningSelected ? "🌙 Evening ✅" : "🌙 Evening", $"workinghours:{selectedDay}:Evening") },
            CreateHourRow(eveningWorkingHours, selectedHours, chatId, selectedDay).ToList()
        };

        // Add copy buttons if there are other selected days
        if (otherDays.Any())
        {
            var copyButtons = new List<InlineKeyboardButton>();
            foreach (var day in otherDays)
            {
                copyButtons.Add(InlineKeyboardButton.WithCallbackData($"📋 Copy to {day}", $"copy_hours:{selectedDay}:{day}"));
            }
            keyboardButtons.Add(copyButtons);
        }

        // Add confirm and clear buttons
        keyboardButtons.Add(new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("✅ Confirm", $"confirm_hours:{selectedDay}"),
            InlineKeyboardButton.WithCallbackData("❌ Clear Selection", $"clear_hours:{selectedDay}")
        });

        var inlineKeyboardMarkup = new InlineKeyboardMarkup(keyboardButtons);

        // Delete previous message before sending a new one
        await DeletePreviousMessage(chatId, cancellationToken);

        var sentMessage = await _botClient.SendMessage(
            chatId: chatId,
            text: messageBuilder.ToString(),
            replyMarkup: inlineKeyboardMarkup,
            cancellationToken: cancellationToken);

        // Store the message ID to delete later
        lastSentMessageIds[chatId] = sentMessage.MessageId;

        return sentMessage;
    }

    // ✅ Helper method to create rows of time slot buttons dynamically
    private static InlineKeyboardButton[] CreateHourRow(string[] hours, List<TimeSpan> selectedHours, long chatId, DayOfWeek selectedDay)
    {
        return hours.Select(hour =>
        {
            TimeSpan timeSpan = TimeSpan.Parse(hour);
            bool isSelected = selectedHours.Contains(timeSpan);
            string buttonText = isSelected ? $"{hour} ✅" : hour;
            return InlineKeyboardButton.WithCallbackData(buttonText, $"workinghours:{selectedDay}:{hour}");
        }).ToArray();
    }



    private async Task DeletePreviousMessage(long chatId, CancellationToken cancellationToken)
    {
        if (lastSentMessageIds.TryGetValue(chatId, out int messageId))
        {
            try
            {
                await _botClient.DeleteMessage(chatId, messageId, cancellationToken);
            }
            catch (ApiRequestException ex)
            {
                _logger.LogWarning($"Failed to delete previous message for chat {chatId}: {ex.Message}");
            }
        }
    }

    private void ToggleSectionSelection(List<TimeSpan> selectedList, string[] sectionHours)
    {
        var sectionTimeSpans = sectionHours.Select(TimeSpan.Parse).ToList();

        // ✅ If all section hours are selected, deselect all
        if (sectionTimeSpans.All(selectedList.Contains))
        {
            selectedList.RemoveAll(sectionTimeSpans.Contains);
        }
        else
        {
            // ✅ Add missing hours
            foreach (var time in sectionTimeSpans)
            {
                if (!selectedList.Contains(time))
                    selectedList.Add(time);
            }
        }
    }

    private async Task ValidateAndSaveToken(long chatId, string tokenValue, CancellationToken cancellationToken)
    {
        var token = await _dbContext.Tokens.Include(t => t.Company).FirstOrDefaultAsync(t => t.TokenValue == tokenValue, cancellationToken);

        if (token == null || token.Used)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Invalid token or company not found. Please check and enter again.",
                replyMarkup: new ForceReplyMarkup { Selective = true },
                cancellationToken: cancellationToken);
            return;
        }

        token.ChatId = chatId;
        token.Used = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        userConversations.TryRemove(chatId, out _);

        // Add persistent menu button after successful token validation
        var replyKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("📋 Menu") }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = false
        };

        await _botClient.SendMessage(
            chatId: chatId,
            text: "✅ Token accepted! Use the Menu button to access all features.",
            replyMarkup: replyKeyboard,
            cancellationToken: cancellationToken);
        await ShowMainMenu(chatId, cancellationToken);
    }

    private async Task ListServices(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
                .ThenInclude(e => e.Services)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null || !company.Employees.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoServices"),
                cancellationToken: cancellationToken);
            return;
        }

        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine(Translations.GetMessage(language, "ListServices"));

        foreach (var employee in company.Employees)
        {
            if (!employee.Services.Any()) continue;

            messageBuilder.AppendLine($"\n👤 {employee.Name}:");
            foreach (var service in employee.Services)
            {
                messageBuilder.AppendLine($"• {service.Name} - {service.Duration.TotalMinutes} min - ${service.Price}");
            }
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BackToMenu"), "back_to_menu") }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: messageBuilder.ToString(),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task StartAddService(long chatId, CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        if (company == null || !company.Employees.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoEmployeeFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var employee = company.Employees.First();
        userInputs[chatId] = new CompanyCreationData
        {
            CompanyName = company.Name,
            SelectedEmployeeId = employee.Id,
            Services = new List<ServiceCreationData>()
        };

        userConversations[chatId] = "WaitingForServiceName";
        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "NewService", employee.Name),
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

    private async Task HandleServiceNameInput(long chatId, string serviceName, CancellationToken cancellationToken)
    {
        var userData = userInputs[chatId];
        if (userData == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: Session expired. Please try again from the main menu.",
                cancellationToken: cancellationToken);
            return;
        }

        if (userData.Services == null)
        {
            userData.Services = new List<ServiceCreationData>();
        }

        userData.Services.Add(new ServiceCreationData { Name = serviceName });
        userConversations[chatId] = "WaitingForServicePrice";

        await _botClient.SendMessage(
            chatId: chatId,
            text: "💵 Enter the price of this service (e.g., 25):",
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

    private async Task HandleCopyHours(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null) return;
        var chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data;

        if (!data.StartsWith("copy_hours:")) return;

        var parts = data.Split(':');
        if (parts.Length != 3) return;

        if (!Enum.TryParse(parts[1], true, out DayOfWeek sourceDay) || 
            !Enum.TryParse(parts[2], true, out DayOfWeek targetDay))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: Invalid day selection.",
                cancellationToken: cancellationToken);
            return;
        }

        if (!userHoursSelections.ContainsKey(chatId) || 
            !userHoursSelections[chatId].ContainsKey(sourceDay))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: No hours selected to copy.",
                cancellationToken: cancellationToken);
            return;
        }

        // Copy the hours to the target day
        if (!userHoursSelections[chatId].ContainsKey(targetDay))
        {
            userHoursSelections[chatId][targetDay] = new List<TimeSpan>();
        }

        userHoursSelections[chatId][targetDay] = new List<TimeSpan>(userHoursSelections[chatId][sourceDay]);

        // Refresh the UI for the target day
        await SendReplyWithWorkingHours(chatId, targetDay, cancellationToken);
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

    private async Task HandleWorkingHoursSelection(long chatId, DayOfWeek day, TimeSpan startTime, TimeSpan endTime, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
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

    private async Task HandleTimeSelection(long chatId, int serviceId, DateTime selectedDate, TimeSpan selectedTime, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        
        // Get the service and employee
        var service = await _dbContext.Services
            .Include(s => s.Employee)
                .ThenInclude(e => e.WorkingHours)
            .FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken);

        if (service == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "ServiceNotFound"),
                cancellationToken: cancellationToken);
            return;
        }

        // Check if the selected time is within working hours
        var workingHours = service.Employee.WorkingHours
            .FirstOrDefault(wh => wh.DayOfWeek == selectedDate.DayOfWeek);

        if (workingHours == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NotWorkingOnDay"),
                cancellationToken: cancellationToken);
            return;
        }

        // Check if the selected time is during a break
        if (workingHours.Breaks.Any(b => 
            selectedTime >= b.StartTime && selectedTime < b.EndTime))
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "TimeDuringBreak"),
                cancellationToken: cancellationToken);
            return;
        }

        // Check if the selected time is available
        var bookingTime = selectedDate.Date.Add(selectedTime);
        var existingBooking = await _dbContext.Bookings
            .FirstOrDefaultAsync(b => b.ServiceId == serviceId && 
                                    b.BookingTime == bookingTime, 
                             cancellationToken);

        if (existingBooking != null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "TimeAlreadyBooked"),
                cancellationToken: cancellationToken);
            return;
        }

        // Create the booking
        var booking = new Booking
        {
            ServiceId = serviceId,
            CompanyId = service.Employee.CompanyId,
            ClientId = chatId,
            BookingTime = bookingTime
        };

        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Send confirmation to client
        var localTime = bookingTime.ToLocalTime();
        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "BookingConfirmation",
                service.Name,
                localTime.ToString("dddd, MMMM d, yyyy"),
                localTime.ToString("hh:mm tt")),
            cancellationToken: cancellationToken);

        // Send notification to company owner
        var companyOwnerChatId = service.Employee.Company.Token.ChatId;
        if (companyOwnerChatId.HasValue)
        {
            var companyOwnerLanguage = userLanguages.GetValueOrDefault(companyOwnerChatId.Value, "EN");
            await _botClient.SendMessage(
                chatId: companyOwnerChatId.Value,
                text: Translations.GetMessage(companyOwnerLanguage, "NewBookingNotification",
                    service.Name,
                    chatId.ToString(),
                    localTime.ToString("dddd, MMMM d, yyyy"),
                    localTime.ToString("hh:mm tt")),
                cancellationToken: cancellationToken);
        }

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

