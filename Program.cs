using Microsoft.EntityFrameworkCore;
using Azure.Identity;
using Telegram.Bot;
using Telegram.Bot.Infrastructure.Configs;
using Telegram.Bot.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json;
using Telegram.Bot.Commands;
using Telegram.Bot.Enums;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ✅ Improved Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddAzureWebAppDiagnostics();

var environment = builder.Environment.EnvironmentName;
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// ✅ FIX: Correct Key Vault Config Name (was "AzureVaulConfig")
var keyVaultUri = builder.Configuration["AzureVaultConfig:Url"];
if (!string.IsNullOrEmpty(keyVaultUri) && !builder.Environment.IsDevelopment()) // Only use in production
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

// ✅ Add Application Insights
var applicationInsightsOptions = new ApplicationInsightsServiceOptions
{
    ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"]
};
builder.Services.AddApplicationInsightsTelemetry(applicationInsightsOptions);

// ✅ Setup Bot Configuration
var botConfigurationSection = builder.Configuration.GetSection(BotConfiguration.Configuration);
builder.Services.Configure<BotConfiguration>(botConfigurationSection);

var botConfiguration = botConfigurationSection.Get<BotConfiguration>();

// ✅ FIX: Ensure Bot Token is Set
if (botConfiguration == null || string.IsNullOrEmpty(botConfiguration.Token))
{
    throw new Exception("Telegram Bot Token is missing from configuration.");
}

// ✅ Register Named HttpClient for Resilience
builder.Services.AddHttpClient("telegram_bot_client")
                .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
                {
                    BotConfiguration? botConfig = sp.GetConfiguration<BotConfiguration>();
                    TelegramBotClientOptions options = new(botConfig.Token);
                    return new TelegramBotClient(options, httpClient);
                });

// ✅ Register PostgreSQL Database Context
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BookingDatabase")));

// ✅ Dependency Injection for Services
builder.Services.AddScoped<ClientUpdateHandler>();
builder.Services.AddScoped<CompanyUpdateHandler>();
builder.Services.AddScoped<TokensService>();
builder.Services.AddSingleton<IUserStateService, UserStateService>();
builder.Services.AddSingleton<ICompanyCreationStateService, CompanyCreationStateService>();
builder.Services.AddScoped<IStartCommandHandler, StartCommandHandler>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddHostedService<BookingReminderService>();
#region Command Handlers
builder.Services.AddTransient<BreakCommandHandler>();
builder.Services.AddTransient<GenerateClientLinkHandler>();
builder.Services.AddTransient<ServiceCommandHandler>();
builder.Services.AddTransient<WorkDayCommandHandler>();
builder.Services.AddTransient<WorkTimeCommandHandler>();
builder.Services.AddTransient<ConfirmBookingCommand>();
builder.Services.AddTransient<RejectBookingCommand>();
builder.Services.AddTransient<MainMenuCommandHandler>();

builder.Services.AddScoped<ICallbackCommandFactory>(serviceProvider =>
{
    var factory = new CallbackCommandFactory(serviceProvider);

    factory.RegisterCommand<BreakCommandHandler>(
        "manage_breaks",
        "add_break",
        "remove_break",
        "select_day_for_breaks",
        "remove_break_confirmation",
        "back_to_breaks"
    );
    factory.RegisterCommand<GenerateClientLinkHandler>(
        "get_client_link"
    );
    factory.RegisterCommand<ServiceCommandHandler>(
        "list_services",
        "add_service",
        "service_duration"
    );
    factory.RegisterCommand<ChangeLanguageCommandHandler>(
        CallbackResponses.ChangeLanguage,
        "set_language"
    );
    factory.RegisterCommand<WorkDayCommandHandler>(
        "setup_work_days",
        "workingdays",
        "workingdays_confirm",
        "workingdays_clearSelection"
    );

    factory.RegisterCommand<WorkTimeCommandHandler>(
        "init_work_time",
        "setup_work_time_start",
        "setup_work_time_end",
        "confirm_working_hours",
        "clear_working_hours",
        "change_work_time",
        "select_day_for_work_time_start"
    );

    factory.RegisterCommand<ConfirmBookingCommand>(
        "confirm_booking"
    );
    factory.RegisterCommand<RejectBookingCommand>(
        "reject_booking"
    );
    factory.RegisterCommand<MainMenuCommandHandler>(
        "menu",
        "back_to_menu"
    );

    return factory;
});

#endregion


// ✅ Hosted Service to Manage Webhook
builder.Services.AddHostedService<ConfigureWebhook>();

// ✅ FIX: Enable Request Buffering (Prevents Empty Request Body)
builder.Services.ConfigureTelegramBotMvc();
builder.Services.AddControllers();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("The API is running."))
    .AddNpgSql(builder.Configuration.GetConnectionString("BookingDatabase") ?? "", name: "Database");

var app = builder.Build();

// ✅ FIX: Enable Request Buffering Middleware
app.Use(async (context, next) =>
{
    context.Request.EnableBuffering(); // Allows multiple reads
    await next();
});

// ✅ FIX: Ensure Correct Webhook Route
if (string.IsNullOrEmpty(botConfiguration.Route))
{
    throw new Exception("Bot webhook route is missing from configuration.");
}

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/details", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            }),
            duration = report.TotalDuration
        });

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(result);
    }
});

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();
