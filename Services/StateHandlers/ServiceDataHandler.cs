using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class ServiceDataHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForServiceName", "WaitingFoServiceDescription", "WaitingForServicePrice", "WaitingForCustomDuration" };

    public ServiceDataHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<WorkStartTimeHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService)
    {
    }

    public override async Task HandleAsync(long chatId, string state, string message, CancellationToken cancellationToken)
    {
        var language = UserStateService.GetLanguage(chatId);
        var creationState = CompanyCreationStateService.GetState(chatId);
        switch (state)
        {
            case "WaitingForServiceName":
                var service = creationState.Services.FirstOrDefault(s => s.Id == creationState.CurrentServiceIndex);
                if (service == null)
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                        text: Translations.GetMessage(language, "SessionExpired"),
                        cancellationToken: cancellationToken);
                    return;
                }

                service.Name = message;
                CompanyCreationStateService.UpdateService(chatId, service);
                UserStateService.SetConversation(chatId, "WaitingFoServiceDescription");
                    await BotClient.SendMessage(
                        chatId: chatId,
                    text: Translations.GetMessage(language, "EnterServiceDescription"),
                        cancellationToken: cancellationToken);
                break;
            case "WaitingFoServiceDescription":
                service = creationState.Services.FirstOrDefault(s => s.Id == creationState.CurrentServiceIndex);
                if (service == null)
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                            text: Translations.GetMessage(language, "SessionExpired"),
                        cancellationToken: cancellationToken);
                        return;
                }

                service.Description = message;
                CompanyCreationStateService.UpdateService(chatId, service);
                UserStateService.SetConversation(chatId, "WaitingForServicePrice");
                await BotClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "EnterServicePrice"),
                    cancellationToken: cancellationToken);
                break;

            case "WaitingForServicePrice":
                await HandleServicePriceInput(chatId, message, cancellationToken);
                break;

            case "WaitingForCustomDuration":
                if (!int.TryParse(message, out var customDuration) || customDuration <= 0)
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                        text:Translations.GetMessage(language, "InvalidDuration"),
                        cancellationToken: cancellationToken);
                    return;
                }

                service = CompanyCreationStateService.GetState(chatId).Services.LastOrDefault();
                if (service == null)
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                            text: Translations.GetMessage(language, "SessionExpired"),
                        cancellationToken: cancellationToken);
                    return;
                }

                service.Duration = customDuration;
                CompanyCreationStateService.UpdateService(chatId, service);
                await SaveNewService(chatId, cancellationToken);
                break;
        }
    }

     private async Task SaveNewService(long chatId, CancellationToken cancellationToken)
    {
        var language = UserStateService.GetLanguage(chatId);
        var creationData = CompanyCreationStateService.GetState(chatId);
        if (creationData == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "SessionExpired"),
                cancellationToken: cancellationToken);
            return;
        }

        // Handle adding service to existing employee
        var company = await DbContext.Companies
            .Include(c => c.Employees)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                    text: Translations.GetMessage(language, "NoCompanyFound"),
                cancellationToken: cancellationToken);
                return;
        }

        var employee = await DbContext.Employees
            .Include(e => e.Services)
            .FirstOrDefaultAsync(e => e.CompanyId == company.Id, cancellationToken);

        if (employee == null)
        {
            await BotClient.SendMessage(
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
            await BotClient.SendMessage(
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
            Description = serviceCreationData.Description,
            EmployeeId = employee.Id,
            Employee = employee
        };

        employee.Services.Add(service);
        await DbContext.SaveChangesAsync(cancellationToken);

        await BotClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "ServiceAddedForEmployee", service.Name, employee.Name),
            cancellationToken: cancellationToken);
        
        CompanyCreationStateService.RemoveService(chatId, serviceCreationData.Id);
        UserStateService.RemoveConversation(chatId);
    }

    private async Task HandleServicePriceInput(long chatId, string priceInput, CancellationToken cancellationToken)
    {
        var language = UserStateService.GetConversation(chatId);
        
        if (!decimal.TryParse(priceInput, out decimal price) || price < 0)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "InvalidPrice"),
                cancellationToken: cancellationToken);
            return;
        }

        var state = CompanyCreationStateService.GetState(chatId);
        var service = state.Services.FirstOrDefault(s => s.Id == state.CurrentServiceIndex);
        if (service == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "SessionExpired"),
                cancellationToken: cancellationToken);
            return;
        }

        service.Price = price;
        UserStateService.SetConversation(chatId, "WaitingForServiceDuration");

        var predefinedDurations = new InlineKeyboardMarkup(new[]
        {
            new [] { InlineKeyboardButton.WithCallbackData("10 min", "service_duration:10"), InlineKeyboardButton.WithCallbackData("15 min", "service_duration:15") },
            new [] { InlineKeyboardButton.WithCallbackData("30 min", "service_duration:30"), InlineKeyboardButton.WithCallbackData("45 min", "service_duration:45") },
            new [] { InlineKeyboardButton.WithCallbackData("Custom", "service_duration:custom") }
        });

        await BotClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "ChooseDuration"),
            replyMarkup: predefinedDurations,
            cancellationToken: cancellationToken);
    }
} 