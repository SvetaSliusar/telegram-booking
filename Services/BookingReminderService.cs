using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram.Bot.Services;

public class BookingReminderService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<BookingReminderService> _logger;
    private readonly ITranslationService _translationService;

    public BookingReminderService(
        IServiceProvider serviceProvider,
        ITelegramBotClient botClient,
        ILogger<BookingReminderService> logger,
        ITranslationService translationService)
    {
        _serviceProvider = serviceProvider;
        _botClient = botClient;
        _logger = logger;
        _translationService = translationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

                var companies = await dbContext.Companies
                    .Include(c => c.ReminderSettings)
                    .ToListAsync(stoppingToken);

                foreach (var company in companies)
                {
                    var reminderHours = company.ReminderSettings?.HoursBeforeReminder ?? 24;
                    var reminderTargetTime = DateTime.UtcNow.AddHours(reminderHours);

                    var bookings = await dbContext.Bookings
                        .Include(b => b.Service)
                            .ThenInclude(s => s.Employee)
                        .Include(b => b.Client)
                        .Where(b => b.CompanyId == company.Id &&
                                    b.BookingTime <= reminderTargetTime &&
                                    b.BookingTime >= DateTime.UtcNow &&
                                    !b.ReminderSent &&
                                    b.Status == BookingStatus.Confirmed)
                        .ToListAsync(stoppingToken);

                    foreach (var booking in bookings)
                    {
                        var client = booking.Client;
                        var clientLanguage = client?.Language ?? "EN";
                        var clientTimeZoneId = client?.TimeZoneId ?? "Europe/Lisbon";

                        DateTime localTime;
                        try
                        {
                            var clientTimeZone = TimeZoneInfo.FindSystemTimeZoneById(clientTimeZoneId);
                            localTime = TimeZoneInfo.ConvertTimeFromUtc(booking.BookingTime, clientTimeZone);
                        }
                        catch
                        {
                            localTime = booking.BookingTime.ToLocalTime(); // fallback
                        }

                        var message = _translationService.Get(clientLanguage, "ReminderMessage",
                            EscapeMarkdown(booking.Service.Name),
                            EscapeMarkdown(company.Name),
                            localTime.ToString("dddd, MMMM d, yyyy"),
                            localTime.ToString("HH:mm"));

                        try
                        {
                            await _botClient.SendMessage(
                                chatId: booking.Client.ChatId,
                                text: message,
                                parseMode: ParseMode.MarkdownV2,
                                cancellationToken: stoppingToken);

                            booking.ReminderSent = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send reminder for booking {BookingId}", booking.Id);
                        }
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing booking reminders");
            }

            // Run hourly
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private string EscapeMarkdown(string text) =>
        Regex.Replace(text ?? "", @"([_*\[\]()~`>#+=|{}.!\\-])", @"\\$1");
} 