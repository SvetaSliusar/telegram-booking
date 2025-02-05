using Microsoft.EntityFrameworkCore;
using Azure.Identity;
using Telegram.Bot;
using Telegram.Bot.Controllers;
using Telegram.Bot.Examples.WebHook;
using Telegram.Bot.Examples.WebHook.Infrastructure.Configs;
using Telegram.Bot.Examples.WebHook.Services;
using Telegram.Bot.Services;

var builder = WebApplication.CreateBuilder(args);

// ✅ Improved Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

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
builder.Services.AddSingleton<UserStateService>();

// ✅ Hosted Service to Manage Webhook
builder.Services.AddHostedService<ConfigureWebhook>();

// ✅ FIX: Enable Request Buffering (Prevents Empty Request Body)
builder.Services.ConfigureTelegramBotMvc();
builder.Services.AddControllers();
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

//app.MapBotWebhookRoute<BotController>(route: botConfiguration.Route);
app.MapControllers();

app.Run();
