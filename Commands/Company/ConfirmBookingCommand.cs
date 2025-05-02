using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Services;
using Telegram.Bot.Types.Enums;

namespace Telegram.Bot.Commands.Company
{
    public class ConfirmBookingCommand : ICallbackCommand
    {
        private readonly BookingDbContext _dbContext;
        private readonly ITelegramBotClient _botClient;
        private readonly IUserStateService _userStateService;
        private readonly ITranslationService _translationService;

        public ConfirmBookingCommand(
            BookingDbContext dbContext,
            ITelegramBotClient botClient,
            IUserStateService userStateService,
            ITranslationService translationService)
        {
            _dbContext = dbContext;
            _botClient = botClient;
            _userStateService = userStateService;
            _translationService = translationService;
        }

        public async Task ExecuteAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(callbackQuery?.Data) || callbackQuery?.Message == null) return;
            
            var data = callbackQuery.Data.Split(':');
            if (data.Length < 2) return;

            var bookingId = int.Parse(data[1]);
            var companyLanguage = await _userStateService.GetLanguageAsync(callbackQuery.From.Id, cancellationToken);

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
                    text: _translationService.Get(companyLanguage, "BookingNotFound"),
                    cancellationToken: cancellationToken);
                return;
            }

            booking.Status = BookingStatus.Confirmed;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Notify company (admin who confirmed)
            await _botClient.SendMessage(
                chatId: callbackQuery.Message.Chat.Id,
                text: _translationService.Get(companyLanguage, "BookingConfirmed"),
                cancellationToken: cancellationToken);

            // Notify client
            var clientTimeZoneId = booking.Client.TimeZoneId ?? "Europe/Lisbon"; // safe fallback
            var clientTimezone = TimeZoneInfo.FindSystemTimeZoneById(clientTimeZoneId);
            var localBookingTime = TimeZoneInfo.ConvertTimeFromUtc(booking.BookingTime, clientTimezone);

            // Get client language (safe fallback to English if needed)
            var clientLanguage = await _userStateService.GetLanguageAsync(booking.Client.ChatId, cancellationToken);

            await _botClient.SendMessage(
                chatId: booking.Client.ChatId,
                text: _translationService.Get(clientLanguage, "BookingConfirmedByCompany",
                    booking.Service.Name,
                    booking.Service.Employee.Name,
                    localBookingTime.ToString("dddd, MMMM d, yyyy"),
                    clientTimeZoneId,
                    localBookingTime.ToString("HH:mm")),
                    parseMode: ParseMode.MarkdownV2,
                    cancellationToken: cancellationToken);

            if (booking.Company.Latitude.HasValue && booking.Company.Longitude.HasValue)
            {
                await _botClient.SendLocation(
                    chatId: booking.Client.ChatId,
                    latitude: booking.Company.Latitude.Value,
                    longitude: booking.Company.Longitude.Value,
                    cancellationToken: cancellationToken);
            }
        }

    }
}
