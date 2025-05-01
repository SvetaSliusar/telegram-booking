
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Commands.Client;

public class BookAppointmentCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;

    public BookAppointmentCommandHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _dbContext = dbContext;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;

        await ShowCompaniesSelection(chatId, cancellationToken);
    }

    private async Task ShowCompaniesSelection(long chatId, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var companies = await _dbContext
            .ClientCompanyInvites
            .Include(c => c.Client)
            .Where(c => c.Client.ChatId == chatId)
            .Include(c => c.Company)
            .Select(c => c.Company).ToListAsync(cancellationToken);

        if (!companies.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: Translations.GetMessage(language, "NoCompaniesAvailable"),
                cancellationToken: cancellationToken);
            return;
        }

        var companyButtons = companies.Select(c =>
            new[] { InlineKeyboardButton.WithCallbackData(c.Name, $"choose_company:{c.Id}") }).ToArray();

        var keyboard = new InlineKeyboardMarkup(companyButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: Translations.GetMessage(language, "SelectCompany"),
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }
}
