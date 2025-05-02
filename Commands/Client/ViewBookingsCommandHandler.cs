
using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Telegram.Bot.Commands.Client;

public class ViewBookingsCommandHanlder : ICallbackCommand
{
    private readonly ITelegramBotClient _botClient;
    private readonly IUserStateService _userStateService;
    private readonly BookingDbContext _dbContext;
    private readonly ITranslationService _translationService;

    public ViewBookingsCommandHanlder(
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

        await HandleViewBookings(chatId, cancellationToken);
    }

    private async Task HandleViewBookings(long chatId, CancellationToken cancellationToken)
    {
        var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);
        var client = await _dbContext.Clients
            .FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);

        if (client == null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "NoBookingsFound"), 
                cancellationToken: cancellationToken);
            return;
        }

        var bookings = await _dbContext.Bookings
            .Include(b => b.Service)
                .ThenInclude(s => s.Employee)
            .Include(b => b.Company)
            .Where(b => b.ClientId == client.Id && b.BookingTime >= DateTime.UtcNow && b.Status == BookingStatus.Confirmed)
            .OrderBy(b => b.BookingTime)
            .ToListAsync(cancellationToken);

        if (!bookings.Any())
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "NoUpcomingBookings"),
                replyMarkup: new InlineKeyboardMarkup(new[] 
                { 
                    new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "BackToMenu"), "back_to_menu") }
                }),
                cancellationToken: cancellationToken);
            return;
        }

        var messageBuilder = new StringBuilder();
        messageBuilder.AppendLine(_translationService.Get(language, "UpcomingBookings"));
        messageBuilder.AppendLine();

        var clientTimezoneId = client.TimeZoneId ?? "Europe/Lisbon";
        var clientTimeZone = TimeZoneInfo.FindSystemTimeZoneById(clientTimezoneId);

        foreach (var booking in bookings)
        {
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(booking.BookingTime, clientTimeZone);
            messageBuilder.AppendLine(_translationService.Get(language, "BookingDetails",
                booking.Company.Name,
                booking.Service.Name, 
                booking.Service.Employee.Name,
                localTime.ToString("dddd, MMMM d, yyyy"),
                localTime.ToString("HH:mm"),
                clientTimeZone.Id));
            messageBuilder.AppendLine();
        }

        var message = messageBuilder.ToString();

        await _botClient.SendMessage(
            chatId: chatId,
            text: message, 
            replyMarkup: new InlineKeyboardMarkup(new[] 
            { 
                new[] { InlineKeyboardButton.WithCallbackData(_translationService.Get(language, "BackToMenu"), "back_to_menu") }
            }),
            cancellationToken: cancellationToken);
    }

}
