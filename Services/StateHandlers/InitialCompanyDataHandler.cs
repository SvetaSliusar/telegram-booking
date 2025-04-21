using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Services.StateHandlers;

public class InitialCompanyDataHandler : BaseStateHandler
{
    public override List<string> StateNames => new List<string> {  "WaitingForCompanyName", "WaitingForEmployeeName" };

    public InitialCompanyDataHandler(
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
        switch (state)
        {
            case "WaitingForCompanyName":
                CompanyCreationStateService.SetCompanyName(chatId, message);
                CompanyCreationStateService.SetCompanyAlias(chatId, GenerateCompanyAlias(message));
                UserStateService.SetConversation(chatId, "WaitingForEmployeeName");

                await BotClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "EnterYourName"),
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken);
                break;

            case "WaitingForEmployeeName":
                CompanyCreationStateService.AddEmployee(chatId, new EmployeeCreationData
                {
                    Name = message,
                    Services = new List<int>(),
                    WorkingDays = new List<DayOfWeek>(),
                    WorkingHours = new List<WorkingHoursData>()
                });

                await SaveCompanyData(chatId, cancellationToken);
                CompanyCreationStateService.ClearState(chatId);
                UserStateService.RemoveConversation(chatId);

                await BotClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "BusinessCreated"),
                    cancellationToken: cancellationToken);
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