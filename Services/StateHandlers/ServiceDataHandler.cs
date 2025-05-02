using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class ServiceDataHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForServiceName", "WaitingFoServiceDescription", "WaitingForServicePrice", "WaitingForCustomDuration" };

    public ServiceDataHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<ServiceDataHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService,
        ITranslationService translationService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService, translationService)
    {
    }

    public override async Task HandleAsync(long chatId, string state, Message message, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        var messageText = message.Text;
        if (string.IsNullOrWhiteSpace(messageText))
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "InvalidInput"),
                cancellationToken: cancellationToken);
            return;
        }

        var creationState = CompanyCreationStateService.GetState(chatId);
        switch (state)
        {
            case "WaitingForServiceName":
                var service = creationState.Services.FirstOrDefault(s => s.Id == creationState.CurrentServiceIndex);
                if (service == null)
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                        text: TranslationService.Get(language, "SessionExpired"),
                        cancellationToken: cancellationToken);
                    return;
                }

                service.Name = messageText;
                CompanyCreationStateService.UpdateService(chatId, service);
                UserStateService.SetConversation(chatId, "WaitingFoServiceDescription");
                    await BotClient.SendMessage(
                        chatId: chatId,
                    text: TranslationService.Get(language, "EnterServiceDescription"),
                        cancellationToken: cancellationToken);
                break;
            case "WaitingFoServiceDescription":
                service = creationState.Services.FirstOrDefault(s => s.Id == creationState.CurrentServiceIndex);
                if (service == null)
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                            text: TranslationService.Get(language, "SessionExpired"),
                        cancellationToken: cancellationToken);
                        return;
                }

                service.Description = messageText;
                CompanyCreationStateService.UpdateService(chatId, service);
                await ShowPriceCurrency(chatId, cancellationToken);
                break;

            case "WaitingForServicePrice":
                await HandleServicePriceInput(chatId, messageText, cancellationToken);
                break;

            case "WaitingForCustomDuration":
                if (!int.TryParse(messageText, out var customDuration) || customDuration <= 0)
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                        text:TranslationService.Get(language, "InvalidDuration"),
                        cancellationToken: cancellationToken);
                    return;
                }

                service = CompanyCreationStateService.GetState(chatId).Services.LastOrDefault();
                if (service == null)
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                            text: TranslationService.Get(language, "SessionExpired"),
                        cancellationToken: cancellationToken);
                    return;
                }

                service.Duration = customDuration;
                CompanyCreationStateService.UpdateService(chatId, service);
                await SaveNewService(chatId, cancellationToken);
                await HandleServiceCreationAsync(language, chatId, cancellationToken);
                break;
        }
    }

     private async Task SaveNewService(long chatId, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        var creationData = CompanyCreationStateService.GetState(chatId);
        if (creationData == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "SessionExpired"),
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
                    text: TranslationService.Get(language, "NoCompanyFound"),
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
                    text: TranslationService.Get(language, "NoEmployeeFound"),
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
                text: TranslationService.Get(language, "SessionExpired"),
                cancellationToken: cancellationToken);
            return;
        }

        var service = new Service
        {
            Name = serviceCreationData.Name,
            Price = serviceCreationData.Price,
            Duration = TimeSpan.FromMinutes(serviceCreationData.Duration),
            Description = serviceCreationData.Description,
            Currency = serviceCreationData.Currency,
            EmployeeId = employee.Id,
            Employee = employee
        };

        employee.Services.Add(service);
        await DbContext.SaveChangesAsync(cancellationToken);

        await BotClient.SendMessage(
            chatId: chatId,
            text: TranslationService.Get(language, "ServiceAddedForEmployee", service.Name, employee.Name),
            cancellationToken: cancellationToken);
        
        CompanyCreationStateService.RemoveService(chatId, serviceCreationData.Id);
        UserStateService.RemoveConversation(chatId);
    }

    private async Task ShowPriceCurrency(long chatId, CancellationToken cancellationToken)
    {
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        var state = CompanyCreationStateService.GetState(chatId);
        var service = state.Services.FirstOrDefault(s => s.Id == state.CurrentServiceIndex);
        if (service == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "SessionExpired"),
                cancellationToken: cancellationToken);
            return;
        }

        var currencyKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("USD", $"service_currency:{Currency.USD}") },
            new[] { InlineKeyboardButton.WithCallbackData("EUR", $"service_currency:{Currency.EUR}") },
            new[] { InlineKeyboardButton.WithCallbackData("UAH", $"service_currency:{Currency.UAH}") }
        });

        await BotClient.SendMessage(
            chatId: chatId,
            text: TranslationService.Get(language, "ChooseCurrency"),
            replyMarkup: currencyKeyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleServicePriceInput(long chatId, string priceInput, CancellationToken cancellationToken)
    {
        var language = UserStateService.GetConversation(chatId);
        
        if (!decimal.TryParse(priceInput, out decimal price) || price < 0)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "InvalidPrice"),
                cancellationToken: cancellationToken);
            return;
        }

        var state = CompanyCreationStateService.GetState(chatId);
        var service = state.Services.FirstOrDefault(s => s.Id == state.CurrentServiceIndex);
        if (service == null)
        {
            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "SessionExpired"),
                cancellationToken: cancellationToken);
            return;
        }

        service.Price = price;
        UserStateService.SetConversation(chatId, "WaitingForServiceDuration");

        var predefinedDurations = new InlineKeyboardMarkup(new[]
        {
            new [] { InlineKeyboardButton.WithCallbackData(TranslationService.Get(language, "10min"), "service_duration:10"), 
                     InlineKeyboardButton.WithCallbackData(TranslationService.Get(language, "15min"), "service_duration:15") },
            new [] { InlineKeyboardButton.WithCallbackData(TranslationService.Get(language, "30min"), "service_duration:30"), 
                     InlineKeyboardButton.WithCallbackData(TranslationService.Get(language, "45min"), "service_duration:45") },
            new [] { InlineKeyboardButton.WithCallbackData(TranslationService.Get(language, "Custom"), "service_duration:custom") }
        });

        await BotClient.SendMessage(
            chatId: chatId,
            text: TranslationService.Get(language, "ChooseDuration"),
            replyMarkup: predefinedDurations,
            cancellationToken: cancellationToken);
    }

        private async Task HandleServiceCreationAsync(string language, long chatId, CancellationToken cancellationToken)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(TranslationService.Get(language, "AddService"), "add_service") },
                new[] { InlineKeyboardButton.WithCallbackData(TranslationService.Get(language, "BackToMenu"), "back_to_menu") }
            });

            await BotClient.SendMessage(
                chatId: chatId,
                text: TranslationService.Get(language, "TheNextStep"),
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
} 