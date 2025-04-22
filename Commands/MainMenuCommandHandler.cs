using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Enums;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Commands;

public class MainMenuCommandHandler : ICallbackCommand, IMainMenuCommandHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;

    public MainMenuCommandHandler(
        IUserStateService userStateService, 
        BookingDbContext dbContext,
        ITelegramBotClient botClient)
    {
        _userStateService = userStateService;
        _dbContext = dbContext;
        _botClient = botClient;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null)
        {
            return;
        }
        long chatId = callbackQuery.Message.Chat.Id;

        await ShowMainMenuAsync(chatId, cancellationToken);
    }

    public async Task ShowMainMenuAsync(long chatId, CancellationToken cancellationToken)
    {
        var language = _userStateService.GetLanguage(chatId);
        var role = await GetUserRoleAsync(chatId, cancellationToken);
        switch (role)
        {
            case UserRole.Client:
                await ShowClientMainMenuAsync(chatId, language, cancellationToken);
                break;
            case UserRole.Company:
                await ShowCompanyMainMenuAsync(chatId, language, cancellationToken);
                break;
            default:
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: Translations.GetMessage(language, "UnknownRole"),
                    cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task<UserRole> GetUserRoleAsync(long chatId, CancellationToken cancellationToken)
    {
        var isClient = await _dbContext.Clients.AnyAsync(c => c.ChatId == chatId, cancellationToken);
        if (isClient)
            return UserRole.Client;

        var isCompany = await _dbContext.Tokens
            .Include(t => t.Company)
            .AnyAsync(t => t.ChatId == chatId, cancellationToken);
        if (isCompany)
            return UserRole.Company;

        return UserRole.Unknown;
    }

    private async Task ShowClientMainMenuAsync(long chatId, string language, CancellationToken cancellationToken)
    {
        var buttons = new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "BookAppointment"), "book_appointment") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "MyBookings"), "view_bookings") },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ChangeLanguage"), CallbackResponses.ChangeLanguage) },
            new[] { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ChangeTimezone"), "change_timezone") }
        };

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "MainMenu"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task ShowCompanyMainMenuAsync(long chatId, string language, CancellationToken cancellationToken)
    {
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
}