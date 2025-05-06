using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot.Commands.Client;
using Telegram.Bot.Enums;
using Telegram.Bot.Infrastructure.Configs;
using Telegram.Bot.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;
using static Telegram.Bot.Commands.Helpers.RoleHandler;

namespace Telegram.Bot.Commands.Common;

public class MainMenuCommandHandler : ICallbackCommand, IMainMenuCommandHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ITranslationService _translationService;
    private readonly IRequestContactHandler _contactHandler;
    private readonly BotConfiguration _botConfig;

    public MainMenuCommandHandler(
        IUserStateService userStateService, 
        BookingDbContext dbContext,
        ITelegramBotClient botClient,
        IOptions<BotConfiguration> botOptions,
        ITranslationService translationService,
        IRequestContactHandler contactHandler)
    {
        _translationService = translationService;
        _userStateService = userStateService;
        _dbContext = dbContext;
        _botClient = botClient;
        _contactHandler = contactHandler;
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
                text: _translationService.Get(language, "NoClientFound"),
                cancellationToken: cancellationToken);
            return;
        }

        var isDemoClient = client.CompanyInvites?.Count == 1 &&
            client.CompanyInvites.FirstOrDefault()?.Company.Alias.ToLower() == "demo";

        if (isDemoClient)
        {
            var demoButtons = new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "BookAppointment"), "book_appointment") },
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "MyBookings"), "view_bookings") },
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ChangeLanguage"), CallbackResponses.ChangeLanguage) },
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ChangeTimezone"), "change_timezone") },
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "RequestCompany"), "request_company_creation") },
                new[] { InlineKeyboardButton.WithUrl(_translationService.Get(language, "ContactSupport"), _botConfig.SupportUrl) }
            };

            var demoKeyboard = new InlineKeyboardMarkup(demoButtons);

            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "DemoWelcome"),
                replyMarkup: demoKeyboard,
                cancellationToken: cancellationToken);
        }
        else
        {
            InlineKeyboardButton[][] buttons;
            if (string.IsNullOrEmpty(client.PhoneNumber) && string.IsNullOrEmpty(client.Username))
            {
                await _contactHandler.HandleRequestContactAsync(chatId, cancellationToken);
                return;
            }
            else
            {
            // Normal Client Menu
                buttons = new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "BookAppointment"), "book_appointment") },
                    new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "MyBookings"), "view_bookings") },
                    new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ChangeLanguage"), CallbackResponses.ChangeLanguage) },
                    new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ChangeTimezone"), "change_timezone") }
                };
            }

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
        var company = await _dbContext.Companies
            .Include(c => c.Employees)
            .FirstOrDefaultAsync(c => c.Token.ChatId == chatId, cancellationToken);

        if (company?.PaymentStatus == PaymentStatus.Failed)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "AccessBlockedPaymentFailed"),
                cancellationToken: cancellationToken);

            if (company != null)
            {
                var buttons = new List<List<InlineKeyboardButton>>
                {
                    new()
                    {
                        InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ManageSubscription"), "cancel_subscription")
                    }
                };
                var keyboardButton = new InlineKeyboardMarkup(buttons);

                await _botClient.SendMessage(
                    chatId: chatId,
                    text: _translationService.Get(language, "AccessBlockedPaymentFailed"),
                    replyMarkup: keyboardButton,
                    cancellationToken: cancellationToken);
                return;
            }
            return;
        }

        if (company == null || company.Employees.Count == 0)
        {
            await ShowEmptyCompanyStateAsync(chatId, language, cancellationToken);
            return;
        }

        var keyboardButtons = new List<List<InlineKeyboardButton>>
        {
            new()
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "EditCompany"), "edit_company_menu"),
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ListServices"), "list_services")
            },
            // Row 2: Booking tools
            new()
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "GetClientLink"), "get_client_link"),
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ViewDailyBookings"), "view_daily_bookings")
            },
            // Row 3: Settings and feedback
            new()
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ChangeLanguage"), "change_language"),
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "LeaveFeedback"), "leave_feedback")
            },
            // Row 4: Support
            new()
            {
                InlineKeyboardButton.WithUrl(_translationService.Get(language, "ContactSupport"), _botConfig.SupportUrl),
            },
            // Row 5: Critical action
            new()
            {
                InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "ManageSubscription"), "cancel_subscription")
            }
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