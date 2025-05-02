using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Models;
using Telegram.Bot.Services.Constants;
using Telegram.Bot.Types;

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

                // Get all companies with their reminder settings
                var companies = await dbContext.Companies
                    .Include(c => c.ReminderSettings)
                    .ToListAsync(stoppingToken);

                foreach (var company in companies)
                {
                    var reminderHours = company.ReminderSettings?.HoursBeforeReminder ?? 24; // Default to 24 hours

                    // Calculate the time range for tomorrow's bookings
                    var tomorrow = DateTime.UtcNow.Date.AddDays(1);
                    var reminderTime = DateTime.UtcNow.AddHours(reminderHours);

                    // Get bookings that need reminders
                    var bookings = await dbContext.Bookings
                        .Include(b => b.Service)
                            .ThenInclude(s => s.Employee)
                        .Include(b => b.Client)
                        .Where(b => b.CompanyId == company.Id &&
                                   b.BookingTime.Date == tomorrow &&
                                   b.BookingTime <= reminderTime &&
                                   !b.ReminderSent)
                        .ToListAsync(stoppingToken);

                    foreach (var booking in bookings)
                    {
                        var language = (await dbContext.Clients.FirstOrDefaultAsync(c => c.Id == booking.ClientId))?.Language ?? "EN";
                        var localTime = booking.BookingTime.ToLocalTime();

                        var message = _translationService.Get(language, "ReminderMessage",
                            booking.Service.Name,
                            company.Name,
                            localTime.ToString("dddd, MMMM d, yyyy"),
                            localTime.ToString("hh:mm tt"));

                        try
                        {
                            await _botClient.SendMessage(
                                chatId: booking.ClientId,
                                text: message,
                                cancellationToken: stoppingToken);

                            booking.ReminderSent = true;
                            await dbContext.SaveChangesAsync(stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send reminder for booking {BookingId}", booking.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing booking reminders");
            }

            // Check every hour
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
} 