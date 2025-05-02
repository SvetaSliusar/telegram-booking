
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Commands.Helpers.BreakCommandParser;

namespace Telegram.Bot.Commands.Client;

public class ChooseCompanyCommandHandler : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ITranslationService _translationService;

    public ChooseCompanyCommandHandler(
        ITelegramBotClient botClient,
        IUserStateService userStateService,
        BookingDbContext dbContext,
        ITranslationService translationService)
    {
        _botClient = botClient;
        _userStateService = userStateService;
        _dbContext = dbContext;
        _translationService = translationService;
    }

    public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery?.Message == null || string.IsNullOrWhiteSpace(callbackQuery.Data))
            return;
        var chatId = callbackQuery.Message.Chat.Id;
        var (_, data) = SplitCommandData(callbackQuery.Data);

        var isParsed = int.TryParse(data, out var companyId);

        if (!isParsed)
        {
           await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(await _userStateService.GetLanguageAsync(chatId, cancellationToken), "InvalidCompanySelection"),
                cancellationToken: cancellationToken);
            return;
        }

        await HandleCompanySelection(chatId, companyId, cancellationToken);
    }

    public async Task HandleCompanySelection(long chatId, int companyId, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

        if (company == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "NoCompanyFound"), 
                cancellationToken: cancellationToken);
            return;
        }
        var services = await (from s in _dbContext.Services
                      join e in _dbContext.Employees on s.EmployeeId equals e.Id
                      where e.CompanyId == company.Id
                      select s).ToListAsync(cancellationToken);


        if (services == null || !services.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "NoServicesAvailable", company.Name), 
                cancellationToken: cancellationToken);
            return;
        }

        var serviceButtons = services
            .Select(service => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{service.Name} - {service.Price:0.##} {service.Currency}", 
                    $"choose_service:{service.Id}")
            })
            .ToArray();


        InlineKeyboardMarkup serviceKeyboard = new(serviceButtons);

        await _botClient.SendMessage(
            chatId: chatId,
            text: _translationService.Get(language, "CompanyServices", company.Name),
            replyMarkup: serviceKeyboard,
            cancellationToken: cancellationToken);
    }
}
