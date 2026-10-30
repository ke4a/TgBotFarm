# TgBotFarm

A scalable multi-bot management platform for hosting and managing Telegram bots built with .NET 10, Blazor, and MongoDB.

## 🎯 Purpose

BotFarm is designed to host and manage multiple Telegram bots within a single application instance. It provides:

- **Centralized bot management** - Run multiple Telegram bots from one application
- **Web-based dashboard** - Real-time bot monitoring and control: turn bots on/off, view logs, check health
- **Automated backups** - Nightly local database backups, with manual create/download/restore/delete from the dashboard
- **Docker support** - Docker Compose configuration for easy local development
- **Localization** - Multi-language support with JSON-based translations
- **Webhook support** - Multiple webhook configuration options (production domain, Visual Studio Dev Tunnels, LocalTunnel, ngrok)
- **Notification system** - Telegram notifications for critical events
- **Security** - ASP.NET Core Identity login for the admin dashboard; webhook requests authenticated with a secret token

## 🏗️ Project Structure

The solution consists of the following projects:

### Core Projects

- **BotFarm** - Main ASP.NET Core web application with Blazor UI
  - Dashboard for application monitoring (logs, memory, uptime, application shutdown)
  - Bot lifecycle controls (turn a bot on/off, retry after an error)
  - Log viewer
  - Authentication and authorization
  - Health checks (UI at `/health-ui`)

- **BotFarm.Core** - Shared core library containing:
  - `BotService` - Base class for implementing bot services
  - `UpdateService` - Base class for handling Telegram updates
  - `MongoDbDatabaseService` - Base class for MongoDB database operations
  - `MongoDbBackupService` - Database backup service
  - `BotControlCoordinator` - Applies and persists each bot's on/off state
  - `JsonLocalizationService` - Localization support
  - `TelegramNotificationService` - Telegram notification service
  - `MarkupService` - Base class for building reply markup
  - Shared models

- **BotFarm.Shared** - Shared Blazor components (per-bot dashboard widgets for stats, chats and backups), layout, and `UpdateControllerBase`

### Bot Projects

- **TestBot** - Example bot implementation demonstrating the framework. Serves as a template for creating new bots. Features:
  - Change bot language on `/changelanguage` command
  - Remember last GIF sent by user
  - Show welcome message on `/start` command
  - Send last GIF on `/getlastgif` command
  - Clear saved data on `/clearchatdata` command
  - Bot dashboard page at `/testbot/dashboard`

### Tests

The `tests/` folder contains NUnit + NSubstitute unit test projects for each project, plus `BotFarm.TestKit` with shared test helpers. Run them with:

```bash
dotnet test
```

## 🚀 Getting Started

Requirements: .NET 10 SDK and Docker (or your own MongoDB instance).

1. Copy `.env.example` to `.env` and adjust it if needed.
2. Set at least one bot's `Token`, `Handle` and `AdminChatId` in `BotFarm/appsettings.json` (or via user secrets / environment variables, e.g. `Bots__TestBot__BotConfig__Token`), and set `Enabled` to `true`.
3. Run `docker compose up`. This starts MongoDB, the tunnel container and the app in the `Development` environment; the dashboard is available at `http://localhost:9090`.

Logs and backups are mounted to the `./logs` and `./backups` folders.

For production, `docker-compose.yml` alone runs only the app container; provide `MONGODB_CONNECTION_STRING`, `WEBHOOK_URL` (a public HTTPS base URL) and optionally `SETUP_CODE` via `.env` or your secrets manager.

## ⚙️ Configuration

### appsettings.json

```json
{
  "ScheduledJobs": {
    "ShutdownEveryHours": 0  // Optional periodic shutdown (0 = disabled)
  },
  "ConnectionStrings": {
    "MongoDb": "***"  // MongoDB connection string
  },
  "WebHookUrl": "https://***", // Base URL for webhook registration
  "Setup": {
    "Code": ""                 // Optional fixed first-run admin setup code (see Authentication)
  },
  "Bots": {
    "TestBot": {               // Bot identifier
      "BotConfig": {
        "Enabled": false,      // Initial on/off state (first run only, see below)
        "Emoji": "👾",        // Bot emoji identifier
        "Token": "***",        // Telegram bot token from @BotFather
        "Handle": "***",       // Bot handle (without @)
        "AdminChatId": 0,      // Telegram chat ID for admin notifications
        "WebhookSecret": ""    // Optional webhook secret (A-Z a-z 0-9 _ -, 1-256 chars); derived from Token when empty
      }
    }
  }
}
```

`Enabled` only seeds a bot's state the first time it is seen. After that, the on/off state is stored in the `BotFarmControl` MongoDB database and changed from the dashboard; editing `Enabled` later has no effect.

`ShutdownEveryHours` only stops the process. Use it together with something that restarts the app, such as Docker's `restart: unless-stopped` policy.

### Scheduled backups

Every bot's database is backed up every night at 05:00 to `backups/<BotName>/` as a zip archive. The 7 most recent archives per bot are kept. Each bot's MongoDB database is named after the bot in lowercase (e.g. `testbot`).

### WebHook URL Options

The `WebHookUrl` setting supports multiple values:

- **Production URL** (e.g., `https://yourdomain.com`) - For production deployments
- **`devtunnel`** - Uses Visual Studio Dev Tunnels (reads from `VS_TUNNEL_URL` environment variable)
- **`localtunnel`** - For Docker Compose with the `tunnel` service running LocalTunnel (reads the static `LOCALTUNNEL_URL` environment variable)
- **`ngrok`** - For Docker Compose with the `tunnel` service running ngrok (queries ngrok's local inspection API at `http://ngrok:4040/api/tunnels` to discover the public URL dynamically)

The `devtunnel`, `localtunnel` and `ngrok` options are only available in the `Development` environment. In any other environment, `WebHookUrl` must be a real URL.

#### Local development tunnel container

The `tunnel` docker-compose service ([`BotFarm/Dockerfile-tunnel`](BotFarm/Dockerfile-tunnel)) bundles both LocalTunnel and ngrok in a single image, switching between them at startup based on the `TUNNEL_PROVIDER` environment variable ([`BotFarm/docker-entrypoint-tunnel.sh`](BotFarm/docker-entrypoint-tunnel.sh)).

The `WEBHOOK_MODE` value in `.env` (default `localtunnel`) sets both the tunnel container's provider and the app's `WebHookUrl`, so they always match.

To use ngrok instead of the default LocalTunnel:

1. Create a free account at [ngrok.com](https://ngrok.com) and copy your authtoken.
2. Set the following in your `.env` file (copy from `.env.example`):
   ```
   NGROK_AUTHTOKEN=your_authtoken_here
   WEBHOOK_MODE=ngrok
   ```

3. Run `docker compose up`.

## 🔐 Authentication

The dashboard is protected with ASP.NET Core Identity.

- On first run, no admin account exists yet, so any request is redirected to `/Account/Setup`, where you create the initial admin username/password.
- Creating that account requires a one-time **setup code**. By default a random code is generated at each startup and logged as a warning (`No admin account exists. Open /Account/Setup and enter setup code: ...`); restarting invalidates the previous code. Alternatively set `Setup:Code` (env `Setup__Code`, or `SETUP_CODE` in `.env` for docker-compose) to a secret of at least 16 characters - its value is never logged. The code stops working once the admin account has been created.
- Afterwards, all requests are redirected to `/Account/Login` until signed in via a secure cookie.
- In the `Development` environment, authentication is bypassed automatically (see `DevelopmentAuthenticationHandler`).
- The internal `/health` endpoint used by the Health Checks UI is authenticated with a random API key generated once per process (not user-facing, requires no configuration).

## 🤖 Creating a New Bot

Create a new Razor class library project (`Microsoft.NET.Sdk.Razor`) referencing `BotFarm.Core` and `BotFarm.Shared`, and reference it from `BotFarm.csproj`. Use [TestBot](TestBot) as the template.

### 1. Create Bot Service

```csharp
public class TestBotService : BotService
{
    public TestBotService(
        ITelegramBotClientFactory clientFactory,
        ILogger<TestBotService> logger,
        IOptionsMonitor<BotConfig> botConfigs,
        IWebhookSecretProvider webhookSecretProvider)
        : base(new BotIdentity(Constants.Name), clientFactory, logger, botConfigs, webhookSecretProvider)
    {
    }

    public override async Task Initialize(CancellationToken cancellationToken = default)
    {
        // Bot-specific initialization
        await base.Initialize(cancellationToken);
    }
}
```

`ITelegramBotClientFactory` is registered by `AddCoreServices` and resolved automatically by DI.

`BotIdentity` carries the bot's name (`Constants.Name` in the example above, which must match the
bot identifier in `appsettings.json`) into the base class, so `Name` and the log prefix are derived
from it automatically.

### 2. Create update handler service

```csharp
public class TestBotUpdateService : UpdateService
{
    public TestBotUpdateService(
        [FromKeyedServices(Constants.Name)] IBotService botService,
        IBotControlService botControlService,
        ILogger<TestBotUpdateService> logger,
        IDatabaseService databaseService,
        ILocalizationService localizationService,
        IMarkupService markupService,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICommandHandler> commandHandlers,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICallbackHandler> callbackHandlers
        /* Inject other required services */)
        : base(new BotIdentity(Constants.Name), botService, botControlService, logger, databaseService,
            localizationService, markupService, commandHandlers, callbackHandlers)
    {
    }

    public override async Task ProcessUpdate(Update update)
    {
        // Handle incoming updates, e.g. detect a command and dispatch it:
        // await HandleCommand(command, update.Message, language);
        // or split callback data such as "key:parameter" and dispatch it:
        // if (TryParseCallbackData(update.CallbackQuery?.Data, out var key, out var parameter)) { ... }
    }
}
```

As a bot grows past a handful of commands, keep `ProcessUpdate` focused on routing by
implementing bot-specific commands/callbacks as separate classes instead of inline
methods. Each command handler implements `ICommandHandler`
(`string Command { get; }`, `Task HandleAsync(Message message, string language)`);
each callback handler implements `ICallbackHandler`
(`string CallbackKey { get; }`, `Task HandleAsync(string callbackId, Message message, User user, string parameter, string language)`).
Register them as keyed services under your bot's name, and the base class's
`HandleCommand`/`HandleCallback` helpers will dispatch to the matching handler:

```csharp
public class GetLastGifCommandHandler : ICommandHandler
{
    public string Command => Constants.Commands.GetLastGif;

    public GetLastGifCommandHandler(/* only the dependencies this command needs */) { }

    public async Task HandleAsync(Message message, string language) { /* ... */ }
}
```

```csharp
services.AddKeyedScoped<ICommandHandler, GetLastGifCommandHandler>(Constants.Name);
```

The two shared framework commands (`/start`, `/changelanguage`) and the
`language-set` callback remain built-in via the protected `Welcome`/`ChangeLanguage`/
`SetLanguage<TSettings>` helpers on `UpdateService` — no handler class needed for those.

### 3. Create update controller

The route must be "api/bot_name/[controller]". Derive from `UpdateControllerBase`, which checks the
webhook secret, waits until the bot is ready, skips updates Telegram has already delivered, and logs and
alerts admins (at most once a minute per bot) when processing an update fails. After a temporary failure
(for example, MongoDB or Telegram unreachable) it answers 503 so Telegram delivers the update again, and
drops the update after 5 such attempts; other failures are acknowledged so later updates are not blocked.

```csharp
[ApiController]
[Route("api/TestBot/[controller]")]
public class UpdateController : UpdateControllerBase
{
    public UpdateController(
        [FromKeyedServices(Constants.Name)] IUpdateService updateService,
        IWebhookSecretProvider webhookSecretProvider,
        IUpdateDeliveryGuard deliveryGuard,
        INotificationService notificationService,
        ILogger<UpdateController> logger)
        : base(updateService, webhookSecretProvider, deliveryGuard, notificationService, logger)
    {
    }
}
```

### 4. Register Bot Services

Create an extension method to register your bot services (see
[TestBot/Extensions/ServiceCollectionExtensions.cs](TestBot/Extensions/ServiceCollectionExtensions.cs) for the full version):

```csharp
public static IServiceCollection AddTestBotServices(this IServiceCollection services, IConfiguration configuration)
{
    // Identity and configuration of the bot
    services.AddSingleton(new BotIdentity(Constants.Name));
    services.Configure<BotConfig>(Constants.Name, configuration.GetSection($"Bots:{Constants.Name}:{nameof(BotConfig)}"));

    // Bot, update handling, commands and callbacks
    services.AddKeyedSingleton<IBotService, TestBotService>(Constants.Name)
            .AddSingleton<IBotService>(s => s.GetRequiredKeyedService<IBotService>(Constants.Name))
            .AddKeyedScoped<IUpdateService, TestBotUpdateService>(Constants.Name)
            .AddKeyedScoped<ICommandHandler, GetLastGifCommandHandler>(Constants.Name)
            .AddKeyedScoped<ICallbackHandler, ClearChatDataCallbackHandler>(Constants.Name)
            .AddScoped<ITestBotMarkupService, TestBotMarkupService>();

    // Database service, also exposed as the keyed IMongoDbDatabaseService used by backups and dashboards
    services.AddSingleton<TestBotDatabaseService>()
            .AddSingleton<IDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>())
            .AddSingleton<IMongoDbDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>())
            .AddKeyedSingleton<IMongoDbDatabaseService>(Constants.Name, (s, _) => s.GetRequiredService<TestBotDatabaseService>())
            .AddSingleton<ITestBotDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>());

    return services;
}
```

Then wire the bot into [`Startup.cs`](BotFarm/Startup.cs):

```csharp
services.AddCoreServices(Configuration, _environment)
    .AddTestBotServices(Configuration);

services.ConfigureHealthChecks(_internalApiKey)
    .AddTestBotHealthChecks();   // builder.AddBotStatsHealthCheck<ITestBotDatabaseService>(Constants.Name)

// in Configure(): make the bot's Razor pages routable
endpoints.MapRazorComponents<App>()
    .AddAdditionalAssemblies(typeof(TestBot.Constants).Assembly)
    ...
```

### 5. Add a dashboard page

The navigation menu links each bot to `/<BotName>/dashboard`. Add a Razor page with that route to your bot
project (see [TestBot/Pages/TestBotDashboard.razor](TestBot/Pages/TestBotDashboard.razor)), reusing the
`DashboardStats`, `DashboardChats` and `DashboardBackups` components from `BotFarm.Shared`.

### 6. Add translations

Put one JSON file per language in `Languages/<BotName>/<locale>.json` in your bot project
(e.g. `Languages/TestBot/en-US.json`); they are copied to the app's output folder and loaded at startup.

### 7. Add Configuration

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

## 🖼️ Admin page screenshots
![Dashboard](./screenshots/dashboard.png)
![Bot](./screenshots/bot.png)

## ❤️ Thanks

- [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) - Telegram Bot API client
- [MudBlazor](https://github.com/MudBlazor/MudBlazor/) - Material Design component library
- [FluentScheduler](https://github.com/fluentscheduler/FluentScheduler) - Job scheduling
- [NLog](https://github.com/NLog/NLog) - Logging framework
- [MongoDB.Driver](https://github.com/mongodb/mongo-csharp-driver) - MongoDB .NET Driver
- [AspNetCore.Diagnostics.HealthChecks](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks) - Health checks UI
- [FluentResults](https://github.com/altmann/FluentResults) - Result handling library
- [SharpZipLib](https://github.com/icsharpcode/SharpZipLib) - Compression library
- [AspNetCore.Identity.MongoDbCore](https://github.com/alexandre-spieser/AspNetCore.Identity.MongoDbCore) - MongoDB provider for ASP.NET Core Identity.
