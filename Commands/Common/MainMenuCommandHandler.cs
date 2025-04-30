using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot.Enums;
using Telegram.Bot.Infrastructure.Configs;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Commands.Common;

public class MainMenuCommandHandler : ICallbackCommand, IMainMenuCommandHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;
    private readonly BotConfiguration _botConfig = new BotConfiguration();

    public MainMenuCommandHandler(
        IUserStateService userStateService, 
        BookingDbContext dbContext,
        ITelegramBotClient botClient,
        IOptions<BotConfiguration> botOptions)
    {
        _userStateService = userStateService;
        _dbContext = dbContext;
        _botClient = botClient;
        if (botOptions != null)
            _botConfig = botOptions.Value;
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

    public async Task ShowClientMainMenuAsync(long chatId, string language, CancellationToken cancellationToken)
    {
        var client = await _dbContext.Clients
            .Include(c => c.CompanyInvites)
                .ThenInclude(i => i.Company)
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (client == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Client not found.",
                cancellationToken: cancellationToken);
            return;
        }

        var isDemoClient = client.CompanyInvites
            .Any(invite => invite.Company.Alias.ToLower() == "demo");

        if (isDemoClient)
        {
            var demoButtons = new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("📸 Book Appointment", "book_appointment") },
                new[] { InlineKeyboardButton.WithCallbackData("📋 My Bookings", "view_bookings") },
                new[] { InlineKeyboardButton.WithCallbackData("🌐 Change Language", CallbackResponses.ChangeLanguage) },
                new[] { InlineKeyboardButton.WithCallbackData("🌎 Change Timezone", "change_timezone") },
                new[] { InlineKeyboardButton.WithUrl("🌟 Learn More", _botConfig.LearMoreUrl) },
                new[] { InlineKeyboardButton.WithCallbackData("🚀 Create My Company", "request_company_creation") }
            };

            var demoKeyboard = new InlineKeyboardMarkup(demoButtons);

            await _botClient.SendMessage(
                chatId: chatId,
                text: "🎉 Welcome to the Demo Company!\n\nExplore how Online Book Set Bot works by trying a test booking.\n\n✨ Want your own booking bot? Click below!",
                replyMarkup: demoKeyboard,
                cancellationToken: cancellationToken);
        }
        else
        {
            // Normal Client Menu
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
    }

    public async Task ShowCompanyMainMenuAsync(long chatId, string language, CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies.FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company == null)
        {
            await ShowEmptyCompanyStateAsync(chatId, language, cancellationToken);
            return;
        }

        var keyboardButtons = new List<List<InlineKeyboardButton>>
            {
                new(){ InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "EditCompany"), "edit_company_menu") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ListServices"), "list_services") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "GetClientLink"), "get_client_link") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ViewDailyBookings"), "view_daily_bookings") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "ChangeLanguage"), "change_language") },
                new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "LeaveFeedback"), "leave_feedback") },
            };

        var keyboard = new InlineKeyboardMarkup(keyboardButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "MainMenu"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task ShowEmptyCompanyStateAsync(long chatId, string language, CancellationToken cancellationToken)
    {
        var welcomeMessage = Translations.GetMessage(language, "WelcomeNoCompany");
        
        var keyboard = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>>
        {
            new() { InlineKeyboardButton.WithCallbackData(Translations.GetMessage(language, "CreateCompany"), "create_company") }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: welcomeMessage,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}