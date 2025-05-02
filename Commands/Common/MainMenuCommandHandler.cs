using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot.Enums;
using Telegram.Bot.Infrastructure.Configs;
using Telegram.Bot.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;
using static Telegram.Bot.Commands.Helpers.RoleHandler;

namespace Telegram.Bot.Commands.Common;

public class MainMenuCommandHandler : ICallbackCommand, IMainMenuCommandHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;
    private readonly BotConfiguration _botConfig = new BotConfiguration();
    private readonly ITranslationService _translationService;

    public MainMenuCommandHandler(
        IUserStateService userStateService, 
        BookingDbContext dbContext,
        ITelegramBotClient botClient,
        IOptions<BotConfiguration> botOptions,
        ITranslationService translationService)
    {
        _translationService = translationService;
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

        var commandHandlers = new Dictionary<string, Func<long, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
        {
            { "menu", ShowMainMenuAsync },
            { "back_to_menu", ShowActiveMainMenuAsync }
        };

        var (commandKey, data) = SplitCommandData(callbackQuery.Data);
        if (commandHandlers.TryGetValue(commandKey, out var handler))
        {
            await handler(chatId, cancellationToken);
        }
        else if (commandKey == "switch_role")
        {
            await HandleSwitchRoleAsync(chatId, data, cancellationToken);
        }
        else
        {
            await ShowMainMenuAsync(chatId, cancellationToken);
        }
    }

    public async Task HandleSwitchRoleAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        await ExecuteAsync(callbackQuery, cancellationToken);
    }

    private async Task ShowMenuByRoleAsync(long chatId, UserRole role, string language, CancellationToken cancellationToken)
    {
        switch (role)
        {
            case UserRole.Client:
                await ShowClientMainMenuAsync(chatId, language, cancellationToken);
                break;
            case UserRole.Company:
                await ShowCompanyMainMenuAsync(chatId, language, cancellationToken);
                break;
            default:
                if (HasRole(role, UserRole.Client) && HasRole(role, UserRole.Company))
                {
                    await ShowRoleSelectionMenuAsync(chatId, language, cancellationToken);
                }
                else
                {
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: _translationService.Get(language, "UnknownRole"), 
                        cancellationToken: cancellationToken);
                }
                break;
        }
    }

    public async Task ShowActiveMainMenuAsync(long chatId, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var activeRole = await _userStateService.GetActiveRoleAsync(chatId, cancellationToken);
        await ShowMenuByRoleAsync(chatId, activeRole, language, cancellationToken);
    }

    public async Task ShowMainMenuAsync(long chatId, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var role = await _userStateService.GetUserRoleAsync(chatId, cancellationToken);
        await ShowMenuByRoleAsync(chatId, role, language, cancellationToken);
    }


    private async Task HandleSwitchRoleAsync(long chatId, string data, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var newRole = data.ToLower() switch
        {
            "client" => UserRole.Client,
            "company" => UserRole.Company,
            _ => UserRole.Unknown
        };

        if (newRole == UserRole.Unknown)
            return;

        await _userStateService.SetActiveRoleAsync(chatId, newRole, cancellationToken);

        if (newRole == UserRole.Client)
        {
            await ShowClientMainMenuAsync(chatId, language, cancellationToken);
        }
        else if (newRole == UserRole.Company)
        {
            await ShowCompanyMainMenuAsync(chatId, language, cancellationToken);
        }
    }

    private async Task ShowRoleSelectionMenuAsync(long chatId, string language, CancellationToken cancellationToken)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    _translationService.Get(language, "ContinueAsClient"),
                    "switch_role:client")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    _translationService.Get(language, "ContinueAsCompany"),
                    "switch_role:company")
            }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "ChooseYourRole"),  
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
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
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "BookAppointment"), "book_appointment") },
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "MyBookings"), "view_bookings") },
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ChangeLanguage"), CallbackResponses.ChangeLanguage) },
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ChangeTimezone"), "change_timezone") }
            };

            var keyboard = new InlineKeyboardMarkup(buttons);

            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "MainMenu"),
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
                new(){ InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "EditCompany"), "edit_company_menu") },
                new() { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ListServices"), "list_services") },
                new() { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "GetClientLink"), "get_client_link") },
                new() { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ViewDailyBookings"), "view_daily_bookings") },
                new() { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ChangeLanguage"), "change_language") },
                new() { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "LeaveFeedback"), "leave_feedback") },
            };

        var keyboard = new InlineKeyboardMarkup(keyboardButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "MainMenu"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task ShowEmptyCompanyStateAsync(long chatId, string language, CancellationToken cancellationToken)
    {
        var welcomeMessage = _translationService.Get(language, "WelcomeNoCompany");
        
        var keyboard = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>>
        {
            new() { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "CreateCompany"), "create_company") }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: welcomeMessage,
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}