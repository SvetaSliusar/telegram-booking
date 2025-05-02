using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class InitialCompanyDataHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  
        "WaitingForCompanyName", "WaitingForEmployeeName", "WaitingForCompanyAlias" };

    public InitialCompanyDataHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        ILogger<InitialCompanyDataHandler> logger,
        BookingDbContext dbContext,
        ICompanyCreationStateService companyCreationStateService,
        ITranslationService translationService)
        : base(botClient, userStateService, logger, dbContext, companyCreationStateService, translationService)
    {
    }

    public override async Task HandleAsync(long chatId, string state, Message message, CancellationToken cancellationToken)
    {
        var messageText = message.Text ?? "";
        var language = await UserStateService.GetLanguageAsync(chatId, cancellationToken);
        switch (state)
        {
            case "WaitingForCompanyName":
                CompanyCreationStateService.SetCompanyName(chatId, messageText);
                if (IsEnglish(messageText))
                {
                    CompanyCreationStateService.SetCompanyAlias(chatId, GenerateCompanyAlias(messageText));
                    UserStateService.SetConversation(chatId, "WaitingForEmployeeName");

                    await BotClient.SendMessage(
                        chatId: chatId,
                        text: TranslationService.Get(language, "EnterYourName"),
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    UserStateService.SetConversation(chatId, "WaitingForCompanyAlias");
                    await BotClient.SendMessage(
                        chatId: chatId,
                        text: TranslationService.Get(language, "SendCompanyAlias"),
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken);
                }
                break;
            case "WaitingForCompanyAlias":
                if (string.IsNullOrWhiteSpace(messageText) || !IsEnglish(messageText))
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                        text: TranslationService.Get(language, "AliasRequired"),
                        cancellationToken: cancellationToken);
                    return;
                }

                if (await DbContext.Companies.AnyAsync(c => c.Alias == messageText))
                {
                    await BotClient.SendMessage(
                        chatId: chatId,
                        text: TranslationService.Get(language, "AliasAlreadyExists"),
                        cancellationToken: cancellationToken);
                    return;
                }

                CompanyCreationStateService.SetCompanyAlias(chatId, messageText);
                UserStateService.SetConversation(chatId, "WaitingForEmployeeName");

                await BotClient.SendMessage(
                    chatId: chatId,
                    text: TranslationService.Get(language, "EnterYourName"),
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
               break;
            case "WaitingForEmployeeName":
                CompanyCreationStateService.AddEmployee(chatId, new EmployeeCreationData
                {
                    Name = messageText,
                    Services = new List<int>(),
                    WorkingDays = new List<DayOfWeek>(),
                    WorkingHours = new List<WorkingHoursData>()
                });

                await SaveCompanyData(chatId, cancellationToken);
                CompanyCreationStateService.ClearState(chatId);
                UserStateService.RemoveConversation(chatId);

                await BotClient.SendMessage(
                    chatId: chatId,
                    text: TranslationService.Get(language, "DataSaved"),
                    cancellationToken: cancellationToken);
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData(TranslationService.Get(language, "SetupWorkDays"), "setup_work_days") }
                });

               await BotClient.SendMessage(chatId, TranslationService.Get(language, "TheNextStep"), replyMarkup: keyboard, cancellationToken: cancellationToken);
               break;
        }
    }

    private async Task SaveCompanyData(long chatId, CancellationToken cancellationToken)
    {
        var state = CompanyCreationStateService.GetState(chatId);
        var token = await DbContext.Tokens.FirstAsync(t => t.ChatId == chatId);
        
        var company = new Company
        {
            Name = state.CompanyName,
            Alias = state.CompanyAlias,
            TokenId = token.Id,
            Token = token
        };

        company.Employees = state.Employees.Select(e => new Employee
            {
                Name = e.Name,
                Services = new List<Service>(),
                WorkingHours = new List<WorkingHours>(),
                Company = company,
            }).ToList();

        company.ReminderSettings = new ReminderSettings
        {
            HoursBeforeReminder = 24,
            Company = company
        };

        DbContext.Companies.Add(company);
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    private bool IsEnglish(string input) => input.All(c => c <= 127);

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
} 