using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands;

public class ServiceCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly ICompanyCreationStateService _companyCreationStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ILogger<ServiceCommandHandler> _logger;

    private readonly ICallbackCommandFactory _commandFactory;

    public ServiceCommandHandler(
        IUserStateService userStateService, 
        BookingDbContext dbContext,
        ITelegramBotClient botClient,
        ICompanyCreationStateService companyCreationStateService,
        ICallbackCommandFactory commandFactory,
        ILogger<ServiceCommandHandler> logger)
    {
        _userStateService = userStateService;
        _dbContext = dbContext;
        _botClient = botClient;
        _commandFactory = commandFactory;
        _companyCreationStateService = companyCreationStateService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;

        var chatId = callbackQuery.Message.Chat.Id;
        var (commandKey, data) = SplitCommandData(callbackQuery.Data);

        var commandHandlers = new Dictionary<string, Func<long, string, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            {"list_services", HandleListServicesAsync },
            {"add_service", HandleAddServiceAsync },
            {"service_duration", HandleServiceDurationAsync }
        };

        if (commandHandlers.TryGetValue(commandKey, out var handler))
        {
            await handler(chatId, data, cancellationToken);
        }
        else
        {
            _logger.LogError("Unknown break command: {CommandKey}", commandKey);
        }
    }

    private async Task HandleServiceDurationAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var durationValue = data;
        var state = _companyCreationStateService.GetState(chatId);
        var service = state.Services.FirstOrDefault(s => s.Id == state.CurrentServiceIndex);
        if (service == null)
        {
            var mainMenuCommand = _commandFactory.CreateCommand("back_to_menu");
            if (mainMenuCommand != null)
            {
                await mainMenuCommand.ExecuteAsync(new CallbackQuery(), cancellationToken);
            }
            return;
        }

        if (durationValue == "custom")
        {
            var language = _userStateService.GetLanguage(chatId);
            _userStateService.SetConversation(chatId, "WaitingForCustomDuration");

            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "EnterCustomDuration"),
                cancellationToken: cancellationToken);
            return;
        }

        if (int.TryParse(durationValue, out int duration))
        {
            service.Duration = duration;

            await SaveNewService(chatId, service, cancellationToken);

            var mainMenuCommand = _commandFactory.CreateCommand("back_to_menu");
            if (mainMenuCommand != null)
            {
                await mainMenuCommand.ExecuteAsync(new CallbackQuery(), cancellationToken);
            }
        }
    }

    private async Task HandleAddServiceAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);

        var serviceId = _companyCreationStateService.AddService(chatId, new ServiceCreationData
        {
            Name = "New Service",
            Description = "Service description",
            Price = 0,
            Duration = 30
        });
        var state = _companyCreationStateService.GetState(chatId);
        state.CurrentServiceIndex = serviceId;
        
        _userStateService.SetConversation(chatId, "WaitingForServiceName");

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "NewService"),
            cancellationToken: cancellationToken);
    }

    private async Task HandleListServicesAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
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

    private async Task SaveNewService(long chatId, ServiceCreationData serviceCreationData, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);

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

        var service = new Service
        {
            Name = serviceCreationData.Name,
            Duration = TimeSpan.FromMinutes(serviceCreationData.Duration),
            Price = serviceCreationData.Price,
            EmployeeId = employee.Id,
            Description = serviceCreationData.Description,
            Employee = employee
        };

        employee.Services.Add(service);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "ServiceAddedForEmployee", service.Name, employee.Name),
            cancellationToken: cancellationToken);

        _companyCreationStateService.RemoveService(chatId, serviceCreationData.Id);
        _userStateService.RemoveConversation(chatId);
    }
}
