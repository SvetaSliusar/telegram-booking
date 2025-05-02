using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Company;

public class WorkDayCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly ICompanyCreationStateService _companyCreationStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ILogger<WorkDayCommandHandler> _logger;
    private readonly ITranslationService _translationService;

    public WorkDayCommandHandler(
        IUserStateService userStateService, 
        BookingDbContext dbContext,
        ITelegramBotClient botClient,
        ICompanyCreationStateService companyCreationStateService,
        ILogger<WorkDayCommandHandler> logger,
        ITranslationService translationService)
    {
        _userStateService = userStateService;
        _dbContext = dbContext;
        _botClient = botClient;
        _companyCreationStateService = companyCreationStateService;
        _logger = logger;
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
            { "setup_work_days", HandleSetupWorkDaysAsync },
            { "workingdays", HandleAddWorkingDayAsync },
            { "workingdays_confirm", HandleConfirmWorkingDaysAsync },
            { "workingdays_clearSelection", HandleClearSelectionAsync }
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

    public async Task HandleSetupWorkDaysAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

        var company = await _dbContext.Companies
            .Include(c => c.Employees)
                .ThenInclude(e => e.WorkingHours)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        var employee = company?.Employees.FirstOrDefault();
        if (employee == null)
        {
            await _botClient.SendMessage(chatId, _translationService.Get(language, "NoEmployeeSelected"), cancellationToken: cancellationToken);
            return;
        }
        _companyCreationStateService.ClearState(chatId);
        var employeeId = _companyCreationStateService.AddEmployee(chatId, new EmployeeCreationData
        {
            Id = employee.Id,
            Name = employee.Name,
            WorkingDays = employee.WorkingHours.Select(d => d.DayOfWeek).ToList(),
            WorkingHours = employee.WorkingHours.Select(wh => new WorkingHoursData
            {
                DayOfWeek = wh.DayOfWeek,
                StartTime = wh.StartTime,
                EndTime = wh.EndTime
            }).ToList()
        });

        var state = _companyCreationStateService.GetState(chatId);
        state.CurrentEmployeeIndex = employeeId;
        if (!string.IsNullOrEmpty(data))
        {
            _companyCreationStateService.AddWorkingDayToEmployee(chatId, employeeId, Enum.Parse<DayOfWeek>(data));
        }

        await SendReplyWithWorkingDaysAsync(chatId, language, state, cancellationToken);
    }

    private async Task SendReplyWithWorkingDaysAsync(long chatId, string language, CompanyCreationData state, CancellationToken cancellationToken = default)
    {
        var workingDays = state.Employees.FirstOrDefault(e => e.Id == state.CurrentEmployeeIndex)?.WorkingDays ?? new List<DayOfWeek>();

        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine(_translationService.Get(language, "CurrentWorkingDays"));
        messageBuilder.AppendLine(workingDays?.Count != 0
            ? string.Join(", ", workingDays ?? Enumerable.Empty<DayOfWeek>())
            : _translationService.Get(language, "NoDaysSelected"));

        InlineKeyboardMarkup inlineKeyboardMarkup = new InlineKeyboardMarkup(new[]
        {
            CreateDayRow(new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday }, workingDays, language),
            CreateDayRow(new List<DayOfWeek> { DayOfWeek.Wednesday, DayOfWeek.Thursday  }, workingDays, language),
            CreateDayRow(new List<DayOfWeek> { DayOfWeek.Friday, DayOfWeek.Saturday }, workingDays, language),
            CreateDayRow(new List<DayOfWeek> { DayOfWeek.Sunday }, workingDays, language),
            new []
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "Confirm"), "workingdays_confirm"),
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ClearSelection"), "workingdays_clearSelection")
            }
        });

        // Delete previous message before sending a new one
        await DeletePreviousMessage(chatId, cancellationToken);

        var sentMessage = await _botClient.SendMessage(
            chatId: chatId,
            text: messageBuilder.ToString(),
            replyMarkup: inlineKeyboardMarkup,
            cancellationToken: cancellationToken);

        _userStateService.SetLastMessageId(chatId, sentMessage.MessageId);
    }

    private InlineKeyboardButton[] CreateDayRow(List<DayOfWeek> days, List<DayOfWeek> selectedDays, string language)
    {
        return days.Select(day =>
        {
            bool isSelected = selectedDays.Contains(day);
            string dayLabel = _translationService.Get(language, $"{day}");
            string buttonText = isSelected ? $"{dayLabel} ✅" : dayLabel;
            return InlineKeyboardButton.WithCallbackData(buttonText, $"workingdays:{day}");
        }).ToArray();
    }

    public async Task HandleAddWorkingDayAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var day = Enum.Parse<DayOfWeek>(data);

        var state = _companyCreationStateService.GetState(chatId);
        var employeeId = state.CurrentEmployeeIndex;
        _companyCreationStateService.AddWorkingDayToEmployee(chatId, employeeId, day);

        await SendReplyWithWorkingDaysAsync(chatId, language, state, cancellationToken);
    }

    public async Task HandleConfirmWorkingDaysAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var state = _companyCreationStateService.GetState(chatId);
        var selectedDays = state.Employees.FirstOrDefault(e => e.Id == state.CurrentEmployeeIndex)?.WorkingDays;
        if (selectedDays == null || !selectedDays.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ No days selected. Please select at least one.",
                cancellationToken: cancellationToken);
            return;
        }

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "SetupWorkTime"), "init_work_time") }
        });

        await _botClient.SendMessage(chatId, _translationService.Get(language, "TheNextStep"), replyMarkup: keyboard, cancellationToken: cancellationToken);
    }

    public async Task HandleClearSelectionAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var state = _companyCreationStateService.GetState(chatId);
        var employeeId = state.CurrentEmployeeIndex;
        _companyCreationStateService.ClearWorkingDays(chatId, employeeId);
        var existingHours = _dbContext.WorkingHours
            .Where(w => w.EmployeeId == state.CurrentEmployeeIndex);
        if (await existingHours.AnyAsync())
        {
            _dbContext.WorkingHours.RemoveRange(existingHours);
            await _dbContext.SaveChangesAsync();
        }
        await SendReplyWithWorkingDaysAsync(chatId, language, state, cancellationToken);
    }

    private async Task DeletePreviousMessage(long chatId, CancellationToken cancellationToken)
    {
        var messageId = _userStateService.GetLastMessageId(chatId);
        if (messageId != null)
        {
            try
            {
                await _botClient.DeleteMessage(chatId, messageId.Value, cancellationToken);
            }
            catch (ApiRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to delete previous message for chat {ChatId}: {Message}", chatId, ex.Message);
            }
        }
    }
}