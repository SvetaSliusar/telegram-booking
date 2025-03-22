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

    private static readonly string StickerId = "CAACAgIAAxkBAAONZnAGyWndlNzCY-3FkmBvV5Y_hskAAi4AAyRxYhqI6DZDakBDFDUE";
    
    // Thread-safe dictionary for conversation states
    private static ConcurrentDictionary<long, string> userConversations = new ConcurrentDictionary<long, string>();

    public CompanyUpdateHandler(ITelegramBotClient botClient, ILogger<CompanyUpdateHandler> logger, BookingDbContext dbContext)
    {
        _botClient = botClient;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task StartCompanyFlow(long chatId, CancellationToken cancellationToken)
    {
        var existingToken = await _dbContext.Tokens.FirstOrDefaultAsync(t => t.ChatId == chatId, cancellationToken);

        if (existingToken == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "🔑 Please enter your company token to continue.",
                replyMarkup: new ForceReplyMarkup { Selective = true },
                cancellationToken: cancellationToken);

            userConversations[chatId] = "WaitingForToken";
            return;
        }

        await SendMainMenu(chatId, cancellationToken);
    }

    public Mode GetMode(long chatId)
    {
        return _dbContext.Tokens.Any(t => t.ChatId == chatId) || (userConversations.ContainsKey(chatId) && userConversations[chatId] == "WaitingForToken") ? Mode.Company : Mode.Client;
    }

    public async Task ShowRoleSelection(long chatId, CancellationToken cancellationToken)
    {
        InlineKeyboardMarkup inlineKeyboard = new(
            new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🏢 I am a Company", "choose_company"),
                    InlineKeyboardButton.WithCallbackData("👤 I am a Client", "choose_client")
                }
            });

        await _botClient.SendMessage(
            chatId: chatId,
            text: "Welcome! Please select your role:",
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
        if (message?.Text is not { } messageText) return;

        var chatId = message.Chat.Id;
        if (userConversations.TryGetValue(chatId, out var state) && state == "WaitingForToken")
        {
            await ValidateAndSaveToken(chatId, messageText, cancellationToken);
        }
        else if (userConversations.ContainsKey(chatId))
        {
            await HandleCompanyCreationInput(chatId, messageText, cancellationToken);
            return;
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
        else
        {
            token.ChatId = chatId;
            token.Used = true;
            await _dbContext.SaveChangesAsync(cancellationToken);

            userConversations.TryRemove(chatId, out _);

            await _botClient.SendMessage(
                chatId: chatId,
                text: $"✅ Token accepted!",
                cancellationToken: cancellationToken);

            await SendMainMenu(chatId, cancellationToken);
        }
    }

    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null) return;
        var chatId = callbackQuery.Message.Chat.Id;
        string data = callbackQuery.Data;

        if (data.StartsWith("service_duration:"))
        {
            var durationValue = data.Split(':')[1];
            if (durationValue == "custom")
            {
                userConversations[chatId] = "WaitingForCustomDuration";

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "⏳ Enter the custom duration for this service in the format `HH:mm` (e.g., 00:20 for 20 minutes):",
                    cancellationToken: cancellationToken);
            }
            else if (TimeSpan.TryParse(durationValue, out var predefinedDuration))
            {
                var userData = userInputs[chatId];
                var currentEmployee = userData.Employees[userData.CurrentEmployeeIndex];
                currentEmployee.Services.Last().Duration = predefinedDuration.Minutes;

                await ProceedToNextServiceOrEmployee(chatId, userData, cancellationToken);
            }
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
                await SetLanguage(chatId, "EN", cancellationToken);
                break;

            default:
                _logger.LogInformation("Unknown callback data: {CallbackData}", callbackQuery.Data);
                break;
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
        userConversations[chatId] = "WaitingForCompanyName"; // Store conversation state

        await _botClient.SendMessage(
            chatId: chatId,
            text: "🏢 Please enter your **company name**:",
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private static ConcurrentDictionary<long, CompanyCreationData> userInputs = new ConcurrentDictionary<long, CompanyCreationData>();
    // ✅ Step 2: Handle User's Input for Company Details
    private async Task HandleCompanyCreationInput(long chatId, string userMessage, CancellationToken cancellationToken)
    {
        if (!userConversations.TryGetValue(chatId, out var state)) return;

        // Ensure userInputs is initialized for this chat ID
        if (!userInputs.ContainsKey(chatId))
        {
            userInputs[chatId] = new CompanyCreationData();
        }

        var userData = userInputs[chatId];
        var currentEmployee = userData.Employees?.ElementAtOrDefault(userData.CurrentEmployeeIndex);
        switch (state)
        {
            case "WaitingForCompanyName":
                userData.CompanyName = userMessage;
                userConversations[chatId] = "WaitingForEmployeeCount";

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "👥 How many employees does your company have?",
                    cancellationToken: cancellationToken);
                break;

            case "WaitingForEmployeeCount":
                if (!int.TryParse(userMessage, out var employeeCount) || employeeCount < 1)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Please enter a valid number of employees.",
                        cancellationToken: cancellationToken);
                    return;
                }

                userData.EmployeeCount = employeeCount;
                userData.Employees = new List<EmployeeCreationData>();
                userData.CurrentEmployeeIndex = 0;

                userConversations[chatId] = "WaitingForEmployeeName";
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "👤 Enter the **name** of the first employee:",
                    cancellationToken: cancellationToken);
                break;

           case "WaitingForEmployeeName":
                // Ensure the Employees list is initialized
                if (userData.Employees == null)
                    userData.Employees = new List<EmployeeCreationData>();

                // Add the new employee
                userData.Employees.Add(new EmployeeCreationData
                {
                    Name = userMessage,
                    Services = new List<ServiceCreationData>()
                });

                // Move to the next state
                userConversations[chatId] = "WaitingForEmployeeWorkingDays";
               await SendReplyWithWorkingDays(chatId, cancellationToken);
               break;

            case "WaitingForEmployeeWorkingDays":
                // Retrieve the current employee
                var currentEmployeeIndex = userData.Employees.Count - 1;
                if (currentEmployeeIndex < 0 || userData.Employees[currentEmployeeIndex] == null)
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Something went wrong. Please start again.",
                        cancellationToken: cancellationToken);
                    userConversations.Remove(chatId, out _); // Clear the conversation state
                    return;
                }
                await SendReplyWithWorkingDays(chatId, cancellationToken);
                break;
            
            case "WaitingForEmployeeWorkingHours":
                currentEmployee = userData.Employees[userData.CurrentEmployeeIndex];

                var hours = userMessage.Split('-');
                if (hours.Length != 2 || !TimeSpan.TryParse(hours[0], out var startTime) || !TimeSpan.TryParse(hours[1], out var endTime))
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Invalid format. Please enter working hours in the format `HH:mm-HH:mm`.",
                        cancellationToken: cancellationToken);
                    return;
                }

                currentEmployee.WorkingHours = new List<WorkingHoursData>();
                foreach (var day in currentEmployee.WorkingDays)
                {
                    currentEmployee.WorkingHours.Add(new WorkingHoursData
                    {
                        DayOfWeek = day,
                        StartTime = startTime,
                        EndTime = endTime
                    });
                }
                userConversations[chatId] = "WaitingForServiceName";
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: $"🛠 Enter the name of the first service provided by {userData.Employees[userData.CurrentEmployeeIndex].Name}:",
                    cancellationToken: cancellationToken);
                break;
            case "WaitingForServiceName":
                currentEmployee.Services.Add(new ServiceCreationData { Name = userMessage });
                userConversations[chatId] = "WaitingForServicePrice";

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "💵 Enter the price of this service (e.g., 25):",
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

                currentEmployee.Services.Last().Price = price;
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

                currentEmployee.Services.Last().Duration = customDuration;
                await ProceedToNextServiceOrEmployee(chatId, userData, cancellationToken);
                break;

            default:
                break;
        }
    }

    private async Task ProceedToNextServiceOrEmployee(long chatId, CompanyCreationData userData, CancellationToken cancellationToken)
    {
        var currentEmployee = userData.Employees[userData.CurrentEmployeeIndex];

        // Ask for another service for the current employee
        if (currentEmployee.Services.Count < 2) // Example limit of 3 services per employee
        {
            userConversations[chatId] = "WaitingForServiceName";
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"🛠 Enter the name of another service provided by {currentEmployee.Name}:",
                cancellationToken: cancellationToken);
            return;
        }

        // Proceed to the next employee
        if (userData.CurrentEmployeeIndex + 1 < userData.EmployeeCount)
        {
            userData.CurrentEmployeeIndex++;
            userConversations[chatId] = "WaitingForEmployeeName";
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"👤 Enter the name of employee {userData.CurrentEmployeeIndex + 1}:",
                cancellationToken: cancellationToken);
        }
        else
        {
            // Finalize company creation
            userConversations.TryRemove(chatId, out _);
            await SaveCompanyToDatabase(chatId, userData);
            userInputs.TryRemove(chatId, out _);

            await _botClient.SendMessage(
                chatId: chatId,
                text: "✅ Your company, employees, and services have been created successfully!",
                cancellationToken: cancellationToken);

            await SendMainMenu(chatId, cancellationToken);
        }
    }


    // Dictionary to store user-selected working days
    private static ConcurrentDictionary<long, List<string>> userDaysSelections = new ConcurrentDictionary<long, List<string>>();
    private static ConcurrentDictionary<long, int> lastSentMessageIds = new ConcurrentDictionary<long, int>();

    // Available working days
    private static readonly string[] weekDays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

   private async Task<Message> SendReplyWithWorkingDays(long chatId, CancellationToken cancellationToken)
{
    if (!userDaysSelections.ContainsKey(chatId))
        userDaysSelections[chatId] = new List<string>();

    var selectedDays = userDaysSelections[chatId];

    InlineKeyboardMarkup inlineKeyboardMarkup = new InlineKeyboardMarkup(new[]
    {
        CreateDayRow(new[] { "Monday", "Tuesday" }, selectedDays, chatId),
        CreateDayRow(new[] { "Wednesday", "Thursday" }, selectedDays, chatId),
        CreateDayRow(new[] { "Friday", "Saturday" }, selectedDays, chatId),
        CreateDayRow(new[] { "Sunday" }, selectedDays, chatId),
        new []
        {
            InlineKeyboardButton.WithCallbackData("✅ Confirm", "workingdays:Confirm"),
            InlineKeyboardButton.WithCallbackData("❌ Clear Selection", "workingdays:ClearSelection")
        }
    });

    // ✅ Delete previous message before sending a new one
    await DeletePreviousMessage(chatId, cancellationToken);

    var sentMessage = await _botClient.SendMessage(
        chatId: chatId,
        text: "📅 Select your working days (you can select multiple):",
        replyMarkup: inlineKeyboardMarkup,
        cancellationToken: cancellationToken);

    // ✅ Store the message ID to delete later
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
        if (!userHoursSelections.ContainsKey(chatId) || !userHoursSelections[chatId].ContainsKey(selectedDay) ||
            userHoursSelections[chatId][selectedDay].Count == 0)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: $"❌ No time slots selected for {selectedDay}. Please select at least one.",
                cancellationToken: cancellationToken);
            return;
        }
        var userData = userInputs[chatId];
        var currentEmployee = userData.Employees?.ElementAtOrDefault(userData.CurrentEmployeeIndex);

        //var employee = await _dbContext.Employees.FirstOrDefaultAsync(c => c.Company.Token.ChatId == chatId, cancellationToken);
        if (currentEmployee == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Error: No employee found for your company.",
                cancellationToken: cancellationToken);
            return;
        }

        // ✅ Remove existing working hours for this specific day
        // _dbContext.WorkingHours.RemoveRange(
        //     _dbContext.WorkingHours.Where(wh => wh.EmployeeId == employee.Id && wh.DayOfWeek == selectedDay));

        // await _dbContext.SaveChangesAsync(cancellationToken); // Ensure deletion before adding new records

        // ✅ Insert new working hours for the selected day
        if (currentEmployee.WorkingHours == null)
                currentEmployee.WorkingHours = new List<WorkingHoursData>();
        foreach (var timeSlot in userHoursSelections[chatId][selectedDay])
        {
            currentEmployee.WorkingHours.Add(new WorkingHoursData
            {
                DayOfWeek = selectedDay,
                StartTime = timeSlot,
                EndTime = timeSlot.Add(TimeSpan.FromMinutes(30)) // Default to 30-minute slots
            });
        }

        userConversations[chatId] = "WaitingForServiceName";
        await _botClient.SendMessage(
            chatId: chatId,
            text: $"🛠 Enter the name of the first service provided by {userData.Employees[userData.CurrentEmployeeIndex].Name}:",
            cancellationToken: cancellationToken);
    }

    // ✅ Step 3: Save Company to Database
   private async Task SaveCompanyToDatabase(long chatId, CompanyCreationData companyData)
    {
        var company = new Company
        {
            Name = companyData.CompanyName,
            TokenId = _dbContext.Tokens.First(t => t.ChatId == chatId).Id,
            Employees = companyData.Employees.Select(e => new Employee
            {
                Name = e.Name,
                Services = e.Services.Select(s => new Service
                {
                    Duration = TimeSpan.FromMinutes(s.Duration),
                    Description = "Test",
                    Name = s.Name
                }).ToList(),
                WorkingHours = e.WorkingHours.Select(wh => new WorkingHours
                {
                    DayOfWeek = wh.DayOfWeek,
                    StartTime = wh.StartTime,
                    EndTime = wh.EndTime
                }).ToList()
            }).ToList()
        };

        _dbContext.Companies.Add(company);
        await _dbContext.SaveChangesAsync();
    }

    private async Task SendMainMenu(long chatId, CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies.FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        var keyboardButtons = company == null
            ? new List<List<InlineKeyboardButton>>
            {
                new() { InlineKeyboardButton.WithCallbackData("🏢 Create Company", "create_company") }
            }
            : new List<List<InlineKeyboardButton>>
            {
                new() { InlineKeyboardButton.WithCallbackData("📅 Setup Work Days", "setup_work_days") },
                new() { InlineKeyboardButton.WithCallbackData("🕒 Setup Work Time", "setup_work_time") },
                new() { InlineKeyboardButton.WithCallbackData("🌐 Change Language", "change_language") }
            };

        var keyboard = new InlineKeyboardMarkup(keyboardButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: "📌 Main Menu",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task SetLanguage(long chatId, string language, CancellationToken cancellationToken)
    {
        await _botClient.SendMessage(
            chatId: chatId,
            text: $"🌎 Language set to: {language}",
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
        var userData = userInputs[chatId];
        var currentEmployee = userData.Employees?.ElementAtOrDefault(userData.CurrentEmployeeIndex);
        currentEmployee.WorkingDays = selectedDays.Select(day => Enum.TryParse(day.Trim(), true, out DayOfWeek dayOfWeek) ? dayOfWeek : (DayOfWeek?)null)
                .Where(day => day.HasValue)
                .Select(day => day.Value)
                .ToList();
        userConversations[chatId] = "WaitingForEmployeeWorkingHours";
        await SendReplyWithWorkingHours(chatId, currentEmployee.WorkingDays.First(), cancellationToken);

        // var employee = await _dbContext.Employees.FirstOrDefaultAsync(c => c.Company.Token.ChatId == chatId, cancellationToken);
        // if (employee == null)
        // {
        //     await _botClient.SendMessage(
        //         chatId: chatId,
        //         text: "❌ Error: No employee found for your company.",
        //         cancellationToken: cancellationToken);
        //     return;
        // }

        // // ✅ Remove existing working days for this company
        // _dbContext.WorkingHours.RemoveRange(
        //     _dbContext.WorkingHours.Where(wh => wh.EmployeeId == employee.Id)
        // );

        // await _dbContext.SaveChangesAsync(cancellationToken); // Ensure deletion before adding new records

        // // ✅ Insert new working hours (default 9 AM - 6 PM)
        // foreach (var day in selectedDays)
        // {
        //     _dbContext.WorkingHours.Add(new WorkingHours
        //     {
        //         EmployeeId = employee.Id,
        //         DayOfWeek = GetDayOfWeek(day),
        //         StartTime = TimeSpan.FromHours(9), // Default start time
        //         EndTime = TimeSpan.FromHours(18)  // Default end time
        //     });
        // }

        // await _dbContext.SaveChangesAsync(cancellationToken);
        // userDaysSelections.TryRemove(chatId, out _);

        // await _botClient.SendMessage(
        //     chatId: chatId,
        //     text: "✅ Your working days have been saved successfully!",
        //     cancellationToken: cancellationToken);
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
        InlineKeyboardMarkup inlineKeyboardMarkup = new InlineKeyboardMarkup(new[]
        {
            CreateDayRow(new[] { "Monday", "Tuesday" }, chatId, "select_day_for_hours"),
            CreateDayRow(new[] { "Wednesday", "Thursday" }, chatId, "select_day_for_hours"),
            CreateDayRow(new[] { "Friday", "Saturday", "Sunday" }, chatId, "select_day_for_hours"),
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: "📅 Select a day to set working hours:",
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
        if (!userHoursSelections.ContainsKey(chatId))
            userHoursSelections[chatId] = new Dictionary<DayOfWeek, List<TimeSpan>>();

        if (!userHoursSelections[chatId].ContainsKey(selectedDay))
            userHoursSelections[chatId][selectedDay] = new List<TimeSpan>();

        var selectedHours = userHoursSelections[chatId][selectedDay];

        bool morningSelected = morningWorkingHours.All(h => selectedHours.Contains(TimeSpan.Parse(h)));
        bool afternoonSelected = afternoonWorkingHours.All(h => selectedHours.Contains(TimeSpan.Parse(h)));
        bool eveningSelected = eveningWorkingHours.All(h => selectedHours.Contains(TimeSpan.Parse(h)));

        InlineKeyboardMarkup inlineKeyboardMarkup = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(morningSelected ? "🌅 Morning ✅" : "🌅 Morning", $"workinghours:{selectedDay}:Morning") },
            CreateHourRow(morningWorkingHours, selectedHours, chatId, selectedDay),

            new[] { InlineKeyboardButton.WithCallbackData(afternoonSelected ? "🌞 Afternoon ✅" : "🌞 Afternoon", $"workinghours:{selectedDay}:Afternoon") },
            CreateHourRow(afternoonWorkingHours, selectedHours, chatId, selectedDay),

            new[] { InlineKeyboardButton.WithCallbackData(eveningSelected ? "🌙 Evening ✅" : "🌙 Evening", $"workinghours:{selectedDay}:Evening") },
            CreateHourRow(eveningWorkingHours, selectedHours, chatId, selectedDay),

            new []
            {
                InlineKeyboardButton.WithCallbackData("✅ Confirm", $"confirm_hours:{selectedDay}"),
                InlineKeyboardButton.WithCallbackData("❌ Clear Selection", $"clear_hours:{selectedDay}")
            }
        });

        // ✅ Delete previous message before sending a new one
        await DeletePreviousMessage(chatId, cancellationToken);

        var sentMessage = await _botClient.SendMessage(
            chatId: chatId,
            text: $"🕒 Select working hours for {selectedDay} (start and end):",
            replyMarkup: inlineKeyboardMarkup,
            cancellationToken: cancellationToken);

        // ✅ Store the message ID to delete later
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
}

public class CompanyCreationData
{
    public string CompanyName { get; set; }
    public int EmployeeCount { get; set; }
    public List<EmployeeCreationData> Employees { get; set; }
    public int CurrentEmployeeIndex { get; set; }
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
}

public class ServiceCreationData
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int Duration { get; set; }
}
