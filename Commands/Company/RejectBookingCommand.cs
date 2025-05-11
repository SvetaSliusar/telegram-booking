using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Models;
using Telegram.Bot.Services;
using Telegram.Bot.Types.Enums;
using System.Globalization;
using static Telegram.Bot.Commands.Helpers.HtmlHelper;

namespace Telegram.Bot.Commands.Company
{
    public class RejectBookingCommand : ICallbackCommand
    {
        private readonly BookingDbContext _dbContext;
        private readonly ITelegramBotClient _botClient;
        private readonly IUserStateService _userStateService;
        private readonly ITranslationService _translationService;

        public RejectBookingCommand(
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
            if (string.IsNullOrEmpty(callbackQuery.Data)) return;

            var data = callbackQuery.Data.Split(':');
            if (data.Length < 2 || !int.TryParse(data[1], out int bookingId)) return;

            if (callbackQuery.Message == null) return;

            var chatId = callbackQuery.Message.Chat.Id;
            var language = await _userStateService.GetLanguageAsync(chatId, cancellationToken);

            var booking = await _dbContext.Bookings
                .Include(b => b.Service)
                    .ThenInclude(s => s.Employee)
                        .ThenInclude(e => e.Company)
                .Include(b => b.Client)
                .FirstOrDefaultAsync(b => b.Id == bookingId, cancellationToken);

            if (booking == null)
            {
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: _translationService.Get(language, "BookingNotFound"),
                    cancellationToken: cancellationToken);
                return;
            }

            booking.Status = BookingStatus.Rejected;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Notify company
            await _botClient.SendMessage(
                chatId: chatId,
                text: _translationService.Get(language, "BookingRejected"),
                cancellationToken: cancellationToken);

            // Notify client
            var clientTimeZone = TimeZoneInfo.FindSystemTimeZoneById(booking.Client.TimeZoneId);
            var localBookingTime = TimeZoneInfo.ConvertTimeFromUtc(booking.BookingTime, clientTimeZone);

            string date = localBookingTime.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
            string time = localBookingTime.ToString("HH:mm", CultureInfo.InvariantCulture);

            string message = _translationService.Get(language, "BookingRejectedByCompany",
                HtmlEncode(booking.Service.Name),
                HtmlEncode(booking.Service.Employee.Name),
                HtmlEncode(date),
                HtmlEncode(time));

            await _botClient.SendMessage(
                chatId: booking.Client.ChatId,
                text: message,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }
    }
} 