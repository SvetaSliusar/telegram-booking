using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Services;

namespace Telegram.Bot.Commands.Company
{
    public class ConfirmBookingCommand : ICallbackCommand
    {
        private readonly BookingDbContext _dbContext;
        private readonly ITelegramBotClient _botClient;
        private readonly IUserStateService _userStateService;

        public ConfirmBookingCommand(
            BookingDbContext dbContext,
            ITelegramBotClient botClient,
            IUserStateService userStateService)
        {
            _dbContext = dbContext;
            _botClient = botClient;
            _userStateService = userStateService;
        }

        public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(callbackQuery?.Data) || callbackQuery?.Message == null) return;
            
            var data = callbackQuery.Data.Split(':');
            if (data.Length < 2) return;

            var bookingId = int.Parse(data[1]);
            var companyLanguage = _userStateService.GetLanguage(callbackQuery.From.Id);

            var booking = await _dbContext.Bookings
                .Include(b => b.Service)
                    .ThenInclude(s => s.Employee)
                        .ThenInclude(e => e.Company)
                .Include(b => b.Client)
                .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);

            if (booking == null)
            {
                await _botClient.SendMessage(
                    chatId: callbackQuery.Message.Chat.Id,
                    text: Translations.GetMessage(companyLanguage, "BookingNotFound"),
                    cancellationToken: cancellationToken);
                return;
            }

            booking.Status = BookingStatus.Confirmed;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Notify company (admin who confirmed)
            await _botClient.SendMessage(
                chatId: callbackQuery.Message.Chat.Id,
                text: Translations.GetMessage(companyLanguage, "BookingConfirmed"),
                cancellationToken: cancellationToken);

            // Notify client
            var clientTimeZoneId = booking.Client.TimeZoneId ?? "Europe/Lisbon"; // safe fallback
            var clientTimezone = TimeZoneInfo.FindSystemTimeZoneById(clientTimeZoneId);
            var localBookingTime = TimeZoneInfo.ConvertTimeFromUtc(booking.BookingTime, clientTimezone);

            // Get client language (safe fallback to English if needed)
            var clientLanguage = _userStateService.GetLanguage(booking.Client.ChatId);

            await _botClient.SendMessage(
                chatId: booking.Client.ChatId,
                text: Translations.GetMessage(clientLanguage, "BookingConfirmedByCompany",
                    booking.Service.Name,
                    booking.Service.Employee.Name,
                    localBookingTime.ToString("dddd, MMMM d, yyyy"),
                    clientTimeZoneId,
                    localBookingTime.ToString("HH:mm")),
                cancellationToken: cancellationToken);

            await _botClient.SendMessage(
                chatId: booking.Client.ChatId,
                text: Translations.GetMessage(clientLanguage, "Location", booking.Company.Location),
                cancellationToken: cancellationToken);
        }

    }
}
