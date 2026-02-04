# TgBotFarm

A scalable multi-bot management platform for hosting and managing Telegram bots built with .NET 10, Blazor, and MongoDB.

## 🎯 Purpose

BotFarm is designed to host and manage multiple Telegram bots within a single application instance. It provides:

- **Centralized bot management** - Run multiple Telegram bots from one application
- **Web-based dashboard** - Real-time bot monitoring and control
- **Automated backups** - Scheduled local database backups
- **Docker support** - Docker Compose configuration for easy local development
- **Localization** - Multi-language support with JSON-based translations
- **Webhook support** - Multiple webhook configuration options (production domain, Visual Studio Dev Tunnels, LocalTunnel)
- **Notification system** - Telegram notifications for critical events
- **Security** - Basic authentication for admin dashboard

## 🏗️ Project Structure

The solution consists of the following projects:

### Core Projects

- **BotFarm** - Main ASP.NET Core web application with Blazor UI
  - Dashboard for application monitoring
  - Log viewer
  - Authentication and authorization
  - Health checks

- **BotFarm.Core** - Shared core library containing:
  - `BotService` - Base class for implementing bot services
  - `UpdateService` - Base class for handling Telegram updates
  - `MongoDbDatabaseService` - Base class for MongoDB database operations
  - `MongoDbBackupService` - Database backup service
  - `JsonLocalizationService` - Localization support
  - `TelegramNotificationService` - Telegram notification service
  - `MarkupService` - Base class for building reply markup
  - Shared models

- **BotFarm.Shared** - Shared Blazor components and layout

### Bot Projects

- **TestBot** - Example bot implementation demonstrating the framework. Serves as a template for creating new bots. Features:
  - Change bot language
  - Remember last GIF sent by user
  - Show welcome message on `/start` command
  - Send last GIF on `/getlastgif` command
  - Clear saved data on `/clearchatdata` command

## ⚙️ Configuration

### appsettings.json

```json
{
  "ScheduledJobs": {
    "ShutdownEveryHours": 0  // Auto-shutdown interval (0 = disabled)
  },
  "ConnectionStrings": {
    "MongoDb": "***"  // MongoDB connection string
  },
  "AuthenticationConfig": {
    "AdminUser": "***",        // Dashboard admin username
    "AdminPassword": "***"     // Dashboard admin password
  },
  "WebHookUrl": "https://***", // Base URL for webhook registration
  "Bots": {
    "TestBot": {               // Bot identifier
      "BotConfig": {
        "Enabled": false,      // Enable/disable the bot
        "Emoji": "👾",        // Bot emoji identifier
        "Token": "***",        // Telegram bot token from @BotFather
        "Handle": "***",       // Bot handle (without @)
        "AdminChatId": 0       // Telegram chat ID for admin notifications
      }
    }
  }
}
```

### WebHook URL Options

The `WebHookUrl` setting supports multiple values:

- **Production URL** (e.g., `https://yourdomain.com`) - For production deployments
- **`devtunnel`** - Uses Visual Studio Dev Tunnels (reads from `VS_TUNNEL_URL` environment variable)
- **`docker`** - For Docker Compose with LocalTunnel service

## 🤖 Creating a New Bot

### 1. Create Bot Service

```csharp
public class TestBotService : BotService
{
    public TestBotService(
        ILogger<TestBotService> logger,
        IHostApplicationLifetime appLifetime,
        IOptionsMonitor<BotConfig> botConfigs) : base(logger, appLifetime, botConfigs)
    {
        logPrefix = $"[{nameof(TestBotService)}]";
    }

    public override string Name => "TestBot";  // Must match bot identifier in appsettings.json

    public override async Task Initialize()
    {
        // Bot-specific initialization
        await base.Initialize();
    }
}
```

### 2. Create update handler service

```csharp
public class TestBotUpdateService : UpdateService
{
    public TestBotUpdateService(
        ILogger<TestBotUpdateService> logger,
        TestBotService botService,
        /* Inject other required services */)
        : base(logger, botService)
    {
        logPrefix = $"[{nameof(TestBotUpdateService)}]";
    }

    public override string Name => "TestBot";

    public override async Task ProcessUpdate(Update update)
    {
        // Handle incoming updates
    }
}
```

### 3. Create update controller

The route must be "api/bot_name/[controller]". Pass the update object to the update service.

```csharp
[ApiController]
[Route("api/TestBot/[controller]")]
public class TestBotUpdatesController : ControllerBase
{
    private readonly IUpdateService _updateService;

    public TestBotUpdatesController(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update)
    {
        await _updateService.ProcessUpdate(update);
        return Ok();
    }
}
```

### 4. Register Bot Services

Create an extension method to register your bot services:

```csharp
public static IServiceCollection AddTestBotServices(this IServiceCollection services)
{
    services.AddSingleton<IBotService>(s => s.GetRequiredKeyedService<IBotService>("TestBot"));
    services.AddKeyedScoped<IUpdateService, TestBotUpdateService>("TestBot")
    
    return services;
}
```

Don't forget to use it in Startup.cs.

### 5. Add Configuration

Add bot configuration to appsettings.json:

```json
{
  "Bots": {
    "TestBot": {
      "BotConfig": {
        "Enabled": true,
        "Emoji": "👾",
        "Token": "YOUR_BOT_TOKEN",
        "Handle": "your_bot_handle",
        "AdminChatId": 123456789
      }
    }
  }
}
```

## ❤️ Thanks

- [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) - Telegram Bot API client
- [MudBlazor](https://github.com/MudBlazor/MudBlazor/) - Material Design component library
- [FluentScheduler](https://github.com/fluentscheduler/FluentScheduler) - Job scheduling
- [NLog](https://github.com/NLog/NLog) - Logging framework
- [MongoDB.Driver](https://github.com/mongodb/mongo-csharp-driver) - MongoDB .NET Driver
- [ZNetCS.AspNetCore.Authentication.Basic](https://github.com/msmolka/ZNetCS.AspNetCore.Authentication.Basic) - Basic authentication middleware
- [FluentResults](https://github.com/altmann/FluentResults) - Result handling library
- [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) - Compression library