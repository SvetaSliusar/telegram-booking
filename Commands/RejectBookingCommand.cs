using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Models;
using Telegram.Bot.Services;

namespace Telegram.Bot.Commands
{
    public class RejectBookingCommand : ICallbackCommand
    {
        private readonly BookingDbContext _dbContext;
        private readonly ITelegramBotClient _botClient;
        private readonly IUserStateService _userStateService;

        public RejectBookingCommand(
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
            var data = callbackQuery.Data.Split(':');
            if (data.Length < 2) return;

            var bookingId = int.Parse(data[1]);
            var language = _userStateService.GetLanguage(callbackQuery.From.Id);

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
                    text: Translations.GetMessage(language, "BookingNotFound"),
                    cancellationToken: cancellationToken);
                return;
            }

            booking.Status = BookingStatus.Rejected;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Notify company
            await _botClient.SendMessage(
                chatId: callbackQuery.Message.Chat.Id,
                text: Translations.GetMessage(language, "BookingRejected"),
                cancellationToken: cancellationToken);

            // Notify client
            var clientTimeZone = TimeZoneInfo.FindSystemTimeZoneById(booking.Client.TimeZoneId);
            var localBookingTime = TimeZoneInfo.ConvertTimeFromUtc(booking.BookingTime, clientTimeZone);

            await _botClient.SendMessage(
                chatId: booking.Client.ChatId,
                text: Translations.GetMessage(language, "BookingRejectedByCompany",
                    booking.Service.Name,
                    booking.Service.Employee.Name,
                    localBookingTime.ToString("dddd, MMMM d, yyyy"),
                    localBookingTime.ToString("hh:mm tt")),
                cancellationToken: cancellationToken);
        }
    }
} 