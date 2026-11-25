using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BotFarm.Authentication;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MongoDB.Driver;
using CoreConstants = BotFarm.Core.Constants;

namespace BotFarm.IntegrationTests.Support;

/// <summary>
/// Runs the whole BotFarm app in memory, configured as in production, against the test MongoDB server and a fake
/// Telegram Bot API. By default every bot the app references is enabled, with a fake token and admin chat stored as
/// its settings before the app applies bot states.
/// </summary>
/// <remarks>
/// <para>
/// The app runs in the <c>IntegrationTest</c> environment, so it also reads <c>BotFarm/appsettings.IntegrationTest.json</c>
/// when that file exists. A bot that needs settings besides its <c>BotConfig</c> gets them there.
/// </para>
/// <para>
/// The app keeps its data in fixed databases, so <see cref="CreateAsync"/> drops every database on the test server
/// first. Only one app may run at a time.
/// </para>
/// </remarks>
public sealed partial class BotFarmAppFactory : WebApplicationFactory<Program>
{
    public const string AdminChatId = "42";
    public const string SetupCode = "integration-test-setup-code";
    public const string HealthApiKey = "integration-test-health-api-key-0123456789";

    /// <summary>
    /// The host name the app is reached at through its HTTPS proxy.
    /// </summary>
    public const string PublicHost = "botfarm.test";

    /// <summary>
    /// The name of the cookie that keeps an admin signed in.
    /// </summary>
    public const string SignInCookieName = ".AspNetCore.Identity.Application";

    /// <summary>
    /// The databases MongoDB keeps for itself, which are never dropped.
    /// </summary>
    private static readonly string[] ServerDatabases = ["admin", "config", "local"];

    private readonly TempDirectoryScope _dataDirectory = new("BotFarmIntegrationTests");
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly bool _seedBotSettings;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    private BotFarmAppFactory(IReadOnlyDictionary<string, string?>? settings, bool seedBotSettings)
    {
        _settings = settings ?? new Dictionary<string, string?>();
        _seedBotSettings = seedBotSettings;
    }

    /// <summary>
    /// The names of the bots the app references, found the same way the app finds them.
    /// </summary>
    public static IReadOnlyList<string> BotNames { get; } =
        BotModuleDiscovery.Discover(typeof(Program).Assembly).Select(module => module.Name).ToList();

    /// <summary>
    /// The fake Telegram Bot API the app's bots talk to.
    /// </summary>
    public FakeTelegramApi Telegram { get; } = new();

    /// <summary>
    /// What the app logged, from every category and at every level.
    /// </summary>
    public FakeLogCollector Logs => Services.GetFakeLogCollector();

    /// <summary>
    /// Drops every database on the test server, starts the app and waits until every bot accepts updates, or, without
    /// seeded settings, until every bot's state is applied, not configured or failed.
    /// </summary>
    /// <param name="settings">
    /// Configuration values the app reads on top of the test defaults, such as <c>Telemetry:OtlpEndpoint</c>.
    /// </param>
    /// <param name="seedBotSettings">
    /// Whether every bot gets stored settings and is enabled before the app applies bot states. Without them, the bots
    /// start without stored settings; <see cref="TokenOf"/> still names a token the fake Telegram Bot API accepts.
    /// </param>
    /// <param name="enabledBots">
    /// Bots whose saved state is enabled before the app starts, for runs without seeded settings.
    /// </param>
    public static async Task<BotFarmAppFactory> CreateAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        bool seedBotSettings = true,
        IReadOnlyCollection<string>? enabledBots = null)
    {
        var databases = await (await MongoServer.Client.ListDatabaseNamesAsync()).ToListAsync();
        foreach (var database in databases.Except(ServerDatabases))
        {
            await MongoServer.Client.DropDatabaseAsync(database);
        }

        var states = new MongoBotControlStateStore(MongoServer.Client.GetDatabase(MongoBotControlStateStore.DatabaseName));
        foreach (var botName in enabledBots ?? [])
        {
            await states.TryInsertAsync(
                new BotControlState { BotName = botName, DesiredEnabled = true, UpdatedAtUtc = DateTime.UtcNow });
        }

        var factory = new BotFarmAppFactory(settings, seedBotSettings);
        if (seedBotSettings)
        {
            await factory.WaitUntilBotsAsync("accepting updates", factory.BotAcceptsUpdates);
        }
        else
        {
            await factory.WaitUntilBotsAsync("settled", factory.BotIsSettled);
        }

        return factory;
    }

    /// <summary>
    /// The fake token the fake Telegram Bot API accepts for the bot.
    /// </summary>
    public string TokenOf(string botName) => TokenFor(botName);

    /// <summary>
    /// The fake token the fake Telegram Bot API accepts for the bot, known before the app starts.
    /// </summary>
    public static string TokenFor(string botName)
    {
        var index = BotNames.ToList().IndexOf(botName);
        if (index < 0)
        {
            throw new ArgumentException($"The app does not reference bot '{botName}'.", nameof(botName));
        }

        // Each bot gets its own Telegram user ID, which is the part of the token before the colon.
        return $"{100_000_001 + index}:integration-test-token";
    }

    /// <summary>
    /// The path of the bot's webhook, which Telegram posts updates to.
    /// </summary>
    public static string WebhookPath(string botName) => $"/api/{botName}/update";

    /// <summary>
    /// A client that reaches the app at <c>https://<see cref="PublicHost"/></c>, keeps cookies and does not follow
    /// redirects.
    /// </summary>
    public HttpClient CreateBrowser()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"https://{PublicHost}"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    /// <summary>
    /// The secret Telegram sends with every update for the bot.
    /// </summary>
    public string GetWebhookSecret(string botName)
    {
        return Services.GetRequiredService<IWebhookSecretProvider>().GetSecret(botName);
    }

    /// <summary>
    /// Delivers <paramref name="update"/> to the bot's webhook the way Telegram does, with the bot's secret.
    /// </summary>
    public Task<HttpResponseMessage> SendUpdateAsync(string botName, JsonObject update)
    {
        return SendUpdateAsync(botName, update, GetWebhookSecret(botName));
    }

    /// <summary>
    /// Delivers <paramref name="update"/> to the bot's webhook with <paramref name="secret"/>, or with no secret when it
    /// is <see langword="null"/>.
    /// </summary>
    public async Task<HttpResponseMessage> SendUpdateAsync(string botName, JsonObject update, string? secret)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath(botName))
        {
            Content = new StringContent(update.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (secret != null)
        {
            request.Headers.Add(CoreConstants.SecretTokenHeaderName, secret);
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Creates an admin account directly in the identity store.
    /// </summary>
    public async Task CreateAdminAsync(string userName, string password)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await userManager.CreateAsync(new ApplicationUser(userName), password);

        Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    /// <summary>
    /// Reads the antiforgery token from the first form of <paramref name="html"/>.
    /// </summary>
    public static string GetAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenInput().Match(html);
        Assert.That(match.Success, Is.True, "The page has no antiforgery token.");

        return match.Groups["token"].Value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: the app signs admins in with the cookie scheme, as in production.
        builder.UseEnvironment("IntegrationTest");
        builder.UseSetting("ConnectionStrings:MongoDb", MongoServer.ConnectionString);
        builder.UseSetting(AppDataPaths.ConfigurationKey, _dataDirectory.RootPath);
        builder.UseSetting("WebHookUrl", $"https://{PublicHost}");
        builder.UseSetting(SetupCodeService.ConfigurationKey, SetupCode);
        builder.UseSetting(HealthApiKeyAuthenticationDefaults.ConfigurationKey, HealthApiKey);

        foreach (var botName in BotNames)
        {
            var token = TokenFor(botName);
            _tokens[botName] = token;
            Telegram.AddBot(botName, token);
        }

        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITelegramBotClientFactory>();
            services.AddSingleton<ITelegramBotClientFactory>(Telegram);
            services.AddLogging(logging => logging.AddFakeLogging());
            if (_seedBotSettings)
            {
                services.AddHostedService(serviceProvider => new EnabledBotsSeeder(
                    serviceProvider.GetRequiredService<IBotSettingsProvider>(),
                    serviceProvider.GetRequiredService<IBotControlStateStore>(),
                    _tokens));
            }
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _dataDirectory.Dispose();
    }

    private bool BotAcceptsUpdates(string botName) =>
        Services.GetRequiredService<IBotControlService>().CanProcessUpdates(botName);

    private bool BotIsSettled(string botName) =>
        Services.GetRequiredService<IBotControlService>().GetStatuses()
            .Any(status => status.BotName == botName
                           && status.Status is BotRuntimeState.Applied
                               or BotRuntimeState.NotConfigured
                               or BotRuntimeState.Error);

    private async Task WaitUntilBotsAsync(string description, Func<string, bool> condition)
    {
        // Reading the services starts the app.
        var botControl = Services.GetRequiredService<IBotControlService>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            while (!BotNames.All(condition))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            var statuses = string.Join(", ", botControl.GetStatuses().Select(status => $"{status.BotName}: {status.Status}"));
            Assert.Fail($"The bots were not {description} within 30 seconds ({statuses}).");
        }
    }

    /// <summary>
    /// Stores each bot's settings and enables it while the app starts, before bot states are applied once the app
    /// has started.
    /// </summary>
    private sealed class EnabledBotsSeeder(
        IBotSettingsProvider settings,
        IBotControlStateStore states,
        IReadOnlyDictionary<string, string> tokens) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var (botName, token) in tokens)
            {
                await settings.LoadAsync(botName, cancellationToken);
                await settings.PersistAsync(
                    botName,
                    new BotSettingsChange
                    {
                        Token = token,
                        AdminChatId = long.Parse(AdminChatId, CultureInfo.InvariantCulture),
                        ChangedBy = "integration-test"
                    },
                    cancellationToken);
                await states.TryInsertAsync(
                    new BotControlState { BotName = botName, DesiredEnabled = true, UpdatedAtUtc = DateTime.UtcNow },
                    cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)"|value="(?<token>[^"]+)"[^>]*name="__RequestVerificationToken""")]
    private static partial Regex AntiforgeryTokenInput();
}
