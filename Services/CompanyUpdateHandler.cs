using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Examples.WebHook;
using Telegram.Bot.Examples.WebHook.Models;
using Telegram.Bot.Examples.WebHook.Services.Constants;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services;

public class CompanyUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<CompanyUpdateHandler> _logger;
    private readonly BookingDbContext _dbContext;

    //private static readonly string StickerId = "CAACAgIAAxkBAAONZnAGyWndlNzCY-3FkmBvV5Y_hskAAi4AAyRxYhqI6DZDakBDFDUE";
    
    // Thread-safe dictionary for conversation states
    private static ConcurrentDictionary<long, string> userConversations = new ConcurrentDictionary<long, string>();
    private static ConcurrentDictionary<long, string> userLanguages = new ConcurrentDictionary<long, string>();

    public CompanyUpdateHandler(ITelegramBotClient botClient, ILogger<CompanyUpdateHandler> logger, BookingDbContext dbContext)
    {
        _botClient = botClient;
        _logger = logger;
        _dbContext = dbContext;
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

        await SendMainMenu(chatId, cancellationToken);
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

        // Handle Menu button click
        if (userMessage == "/menu")
        {
            await SendMainMenu(chatId, cancellationToken);
            return;
        }

        if (userConversations.ContainsKey(chatId))
        {
            await HandleCompanyCreationInput(chatId, userMessage, cancellationToken);
            return;
        }

        // Handle default message
        await _botClient.SendMessage(
            chatId: chatId,
            text: "Please use the provided buttons or type /menu to see the main menu.",
            cancellationToken: cancellationToken);
    }

    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null) return;
        var chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data;
        var language = userLanguages.GetValueOrDefault(chatId, "EN");

        try
        {
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
                    await SendMainMenu(chatId, cancellationToken);
                    return;

                case "get_client_link":
                    await GenerateClientLink(chatId, cancellationToken);
                    return;
            }

            if (data.StartsWith("select_employee:"))
            {
                await HandleEmployeeSelectionForService(callbackQuery, cancellationToken);
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

            // Handle break time selection
            if (data.StartsWith("break_time:"))
            {
                await HandleBreakTimeSelection(callbackQuery, cancellationToken);
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
                    await _botClient.SendMessage(chatId, "❌ Invalid day selected.", cancellationToken: cancellationToken);
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

                case "change_language":
                    var languageKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("English", "set_language:EN") },
                        new[] { InlineKeyboardButton.WithCallbackData("Українська", "set_language:UK") }
                    });

                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "🌐 Select your language / Оберіть мову:",
                        replyMarkup: languageKeyboard,
                        cancellationToken: cancellationToken);
                    return;

                default:
                    _logger.LogInformation("Unknown callback data: {CallbackData}", callbackQuery.Data);
                    break;
            }

            if (data.StartsWith("set_language:"))
            {
                var selectedLanguage = data.Split(':')[1];
                await SetLanguage(chatId, selectedLanguage, cancellationToken);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling callback query");
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ An error occurred. Please try again.",
                cancellationToken: cancellationToken);
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
        if (!userConversations.TryGetValue(chatId, out var state)) return;
        var language = userLanguages.GetValueOrDefault(chatId, "EN");

        // Ensure userInputs is initialized for this chat ID
        if (!userInputs.ContainsKey(chatId))
        {
            userInputs[chatId] = new CompanyCreationData();
        }

        var userData = userInputs[chatId];
        switch (state)
        {
            case "WaitingForCompanyName":
                userData.CompanyName = userMessage;
                // Generate alias from company name
                userData.CompanyAlias = GenerateCompanyAlias(userMessage);
                userData.EmployeeCount = 1; // Set to 1 employee
                userData.Employees = new List<EmployeeCreationData>();
                userData.CurrentEmployeeIndex = 0;
                userConversations[chatId] = "WaitingForEmployeeName";
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "EnterYourName"),
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                break;

            case "WaitingForEmployeeName":
                // Ensure the Employees list is initialized
                if (userData.Employees == null)
                    userData.Employees = new List<EmployeeCreationData>();

                // Add the new employee (business owner)
                userData.Employees.Add(new EmployeeCreationData
                {
                    Name = userMessage,
                    Services = new List<ServiceCreationData>()
                });

                // Finalize company creation
                userConversations.TryRemove(chatId, out _);
                await SaveCompanyData(chatId, userData, cancellationToken);
                userInputs.TryRemove(chatId, out _);

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "BusinessCreated"),
                    cancellationToken: cancellationToken);

                await SendMainMenu(chatId, cancellationToken);
                break;

            case "WaitingForServiceName":
                if (userData.Services == null)
                {
                    userData.Services = new List<ServiceCreationData>();
                }
                userData.Services.Add(new ServiceCreationData { Name = userMessage });
                userConversations[chatId] = "WaitingForServicePrice";

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "EnterServicePrice"),
                    cancellationToken: cancellationToken);
                break;

            case "WaitingForServicePrice":
                if (!decimal.TryParse(userMessage, out var price) || price < 0)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Please enter a valid price.",
                        cancellationToken: cancellationToken);
                    return;
                }

                userData.Services[0].Price = price;
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

                userData.Services[0].Duration = customDuration;
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

    private async Task ProceedToNextServiceOrEmployee(long chatId, CompanyCreationData userData, CancellationToken cancellationToken)
    {
        var currentEmployee = userData.Employees[userData.CurrentEmployeeIndex];

        // Ask for another service for the current employee
        if (currentEmployee.Services.Count < 2) // Example limit of 2 services per employee
        {
            userConversations[chatId] = "WaitingForServiceName";
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"🛠 Enter the name of another service provided by {currentEmployee.Name}:",
                cancellationToken: cancellationToken);
            return;
        }

        // Finalize company creation since we only have one employee
        userConversations.TryRemove(chatId, out _);
        await SaveCompanyData(chatId, userData, cancellationToken);
        userInputs.TryRemove(chatId, out _);

        await _botClient.SendMessage(
            chatId: chatId,
            text: "✅ Your company, employee, and services have been created successfully!",
            cancellationToken: cancellationToken);

        await SendMainMenu(chatId, cancellationToken);
    }


    // Dictionary to store user-selected working days
    private static ConcurrentDictionary<long, List<string>> userDaysSelections = new ConcurrentDictionary<long, List<string>>();
    private static ConcurrentDictionary<long, int> lastSentMessageIds = new ConcurrentDictionary<long, int>();

    // Available working days
    private static readonly string[] weekDays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

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
        // Get the company for this chat ID
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
                text: "❌ Error: No employee found for your company.",
                cancellationToken: cancellationToken);
            return;
        }

        // Create buttons for break time selection
        var breakTimeButtons = new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("5 min", "break_time:5"), InlineKeyboardButton.WithCallbackData("10 min", "break_time:10") },
            new[] { InlineKeyboardButton.WithCallbackData("15 min", "break_time:15"), InlineKeyboardButton.WithCallbackData("20 min", "break_time:20") },
            new[] { InlineKeyboardButton.WithCallbackData("30 min", "break_time:30"), InlineKeyboardButton.WithCallbackData("Custom", "break_time:custom") }
        };

        var keyboard = new InlineKeyboardMarkup(breakTimeButtons);

        // Store the working hours temporarily
        userConversations[chatId] = $"WaitingForBreakTime_{selectedDay}";
        
        // Initialize WorkingHours list if it doesn't exist
        if (currentEmployee.WorkingHours == null)
        {
            currentEmployee.WorkingHours = new List<WorkingHours>();
        }

        // Remove existing working hours for this day
        var existingHours = currentEmployee.WorkingHours.Where(wh => wh.DayOfWeek == selectedDay).ToList();
        foreach (var hour in existingHours)
        {
            currentEmployee.WorkingHours.Remove(hour);
        }

        // Get selected hours and sort them
        var selectedHours = userHoursSelections[chatId][selectedDay].OrderBy(h => h).ToList();
        
        // Ensure we have an even number of hours
        if (selectedHours.Count % 2 != 0)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"❌ Please select pairs of times (start and end) for {selectedDay}. You have selected {selectedHours.Count} times.",
                cancellationToken: cancellationToken);
            return;
        }

        // Create intervals from pairs of times
        var intervals = new List<(TimeSpan Start, TimeSpan End)>();
        for (int i = 0; i < selectedHours.Count; i += 2)
        {
            if (i + 1 < selectedHours.Count)
            {
                intervals.Add((selectedHours[i], selectedHours[i + 1]));
            }
        }

        // Add working hours for each interval
        foreach (var interval in intervals)
        {
            currentEmployee.WorkingHours.Add(new WorkingHours
            {
                DayOfWeek = selectedDay,
                StartTime = interval.Start,
                EndTime = interval.End
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Show the intervals to the user
        var intervalsText = string.Join(", ", intervals.Select(i => $"{i.Start:hh\\:mm}-{i.End:hh\\:mm}"));
        await _botClient.SendMessage(
            chatId: chatId,
            text: $"✅ Working hours for {selectedDay} have been set to:\n{intervalsText}\n\n⏳ Select the break time between services:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleBreakTimeSelection(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null) return;
        var chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data;

        if (!data.StartsWith("break_time:")) return;

        var breakTimeValue = data.Split(':')[1];
        
        // Get the company for this chat ID
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

        var currentEmployee = company.Employees.FirstOrDefault();
        if (currentEmployee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: No employee found for your company.",
                cancellationToken: cancellationToken);
            return;
        }

        if (breakTimeValue == "custom")
        {
            userConversations[chatId] = "WaitingForCustomBreakTime";
            await _botClient.SendMessage(
                chatId: chatId,
                text: "⏳ Enter the custom break time in minutes (e.g., 25):",
                cancellationToken: cancellationToken);
            return;
        }

        if (int.TryParse(breakTimeValue, out int breakMinutes))
        {
            var breakTime = TimeSpan.FromMinutes(breakMinutes);
            foreach (var workingHour in currentEmployee.WorkingHours)
            {
                workingHour.BreakTime = breakTime;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _botClient.SendMessage(
                chatId: chatId,
                text: "✅ Working hours and break time have been updated successfully!",
                cancellationToken: cancellationToken);

            await SendMainMenu(chatId, cancellationToken);
        }
    }

    private async Task HandleCustomBreakTime(long chatId, string message, CancellationToken cancellationToken)
    {
        if (!int.TryParse(message, out int breakMinutes) || breakMinutes <= 0)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Please enter a valid break time in minutes (e.g., 25):",
                cancellationToken: cancellationToken);
            return;
        }

        // Get the company for this chat ID
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

        var currentEmployee = company.Employees.FirstOrDefault();
        if (currentEmployee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: No employee found for your company.",
                cancellationToken: cancellationToken);
            return;
        }

        var breakTime = TimeSpan.FromMinutes(breakMinutes);
        foreach (var workingHour in currentEmployee.WorkingHours)
        {
            workingHour.BreakTime = breakTime;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(
            chatId: chatId,
            text: "✅ Working hours and break time have been updated successfully!",
            cancellationToken: cancellationToken);

        await SendMainMenu(chatId, cancellationToken);
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

    public async Task SendMainMenu(long chatId, CancellationToken cancellationToken)
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
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ListServices"), "list_services") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "AddService"), "add_service") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "GetClientLink"), "get_client_link") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ChangeLanguage"), "change_language") }
            };

        var keyboard = new InlineKeyboardMarkup(keyboardButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "MainMenu"),
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
        
        await SendMainMenu(chatId, cancellationToken);
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

        await SendMainMenu(chatId, cancellationToken);
    }

    // ✅ Helper function to convert string to DayOfWeek enum
    private static DayOfWeek GetDayOfWeek(string day)
    {
        return day.ToLower() switch
        {
            "monday" => DayOfWeek.Monday,
            "tuesday" => DayOfWeek.Tuesday,
            "wednesday" => DayOfWeek.Wednesday,
            "thursday" => DayOfWeek.Thursday,
            "friday" => DayOfWeek.Friday,
            "saturday" => DayOfWeek.Saturday,
            "sunday" => DayOfWeek.Sunday,
            _ => throw new ArgumentException($"Invalid day: {day}")
        };
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
                .Select(h => h.StartTime)
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

        await SendMainMenu(chatId, cancellationToken);
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

        if (company == null || !company.Employees.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ No employee found. Please contact support.",
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
            text: $"🛠 Enter the name of the new service for {employee.Name}:",
            cancellationToken: cancellationToken);
    }

    private async Task HandleEmployeeSelectionForService(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        var employeeId = int.Parse(callbackQuery.Data.Split(':')[1]);

        var company = await _dbContext.Companies
            .Include(c => c.Employees)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        var employee = company?.Employees.FirstOrDefault(e => e.Id == employeeId);
        if (employee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: Employee not found.",
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
            text: $"🛠 Enter the name of the new service for {employee.Name}:",
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
        if (!decimal.TryParse(priceInput, out decimal price) || price < 0)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Please enter a valid price.",
                cancellationToken: cancellationToken);
            return;
        }

        var userData = userInputs[chatId];
        userData.Services[0].Price = price;
        userConversations[chatId] = "WaitingForServiceDuration";

        var predefinedDurations = new InlineKeyboardMarkup(new[]
        {
            new [] { InlineKeyboardButton.WithCallbackData("10 min", "service_duration:10"), InlineKeyboardButton.WithCallbackData("15 min", "service_duration:15") },
            new [] { InlineKeyboardButton.WithCallbackData("30 min", "service_duration:30"), InlineKeyboardButton.WithCallbackData("45 min", "service_duration:45") },
            new [] { InlineKeyboardButton.WithCallbackData("Custom", "service_duration:custom") }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: "⏳ Choose a time duration for this service:",
            replyMarkup: predefinedDurations,
            cancellationToken: cancellationToken);
    }

    private async Task SaveNewService(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var userData = userInputs[chatId];
        if (userData == null || userData.Services == null || !userData.Services.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoServiceDataFound"),
                cancellationToken: cancellationToken);
            return;
        }

        if (userData.IsInitialCreation)
        {
            // Handle initial company creation flow
            var currentEmployee = userData.Employees?.ElementAtOrDefault(userData.CurrentEmployeeIndex);
            if (currentEmployee == null)
            {
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "NoEmployeeFoundInCreation"),
                    cancellationToken: cancellationToken);
                return;
            }

            if (currentEmployee.Services == null)
            {
                currentEmployee.Services = new List<ServiceCreationData>();
            }

            currentEmployee.Services.Add(userData.Services[0]);
            await ProceedToNextServiceOrEmployee(chatId, userData, cancellationToken);
        }
        else
        {
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

            var employee = company.Employees.FirstOrDefault(e => e.Id == userData.SelectedEmployeeId);
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

            var service = new Service
            {
                Name = userData.Services[0].Name,
                Price = userData.Services[0].Price,
                Duration = TimeSpan.FromMinutes(userData.Services[0].Duration),
                Description = "Service description",
                EmployeeId = employee.Id
            };

            employee.Services.Add(service);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "ServiceAddedForEmployee", service.Name, employee.Name),
                cancellationToken: cancellationToken);

            userConversations.Remove(chatId, out _);
            userInputs.Remove(chatId, out _);

            await SendMainMenu(chatId, cancellationToken);
        }
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

    private async Task GenerateClientLink(long chatId, CancellationToken cancellationToken)
    {
        var language = userLanguages.GetValueOrDefault(chatId, "EN");
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: Company not found.",
                cancellationToken: cancellationToken);
            return;
        }

        var botUsername = (await _botClient.GetMeAsync(cancellationToken)).Username;
        var clientLink = $"https://t.me/{botUsername}?start={company.Alias}";

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "ClientLink", clientLink),
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }
}

public class CompanyCreationData
{
    public string CompanyName { get; set; }
    public int EmployeeCount { get; set; }
    public List<EmployeeCreationData> Employees { get; set; }
    public int CurrentEmployeeIndex { get; set; }
    public string CompanyAlias { get; set; }
    public List<ServiceCreationData> Services { get; set; }
    public int? SelectedEmployeeId { get; set; }  // Make nullable to differentiate between modes
    public bool IsInitialCreation => SelectedEmployeeId == null;  // Helper property
}

public class EmployeeCreationData
{
    public string Name { get; set; }
    public List<ServiceCreationData> Services { get; set; }
    public List<DayOfWeek> WorkingDays { get; set; }
    public List<WorkingHoursData> WorkingHours { get; set; }
}

public class WorkingHoursData
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public TimeSpan BreakTime { get; set; }
}

public class ServiceCreationData
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int Duration { get; set; }
}
