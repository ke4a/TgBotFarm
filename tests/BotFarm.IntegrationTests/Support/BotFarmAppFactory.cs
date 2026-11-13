using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BotFarm.Authentication;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using CoreConstants = BotFarm.Core.Constants;

namespace BotFarm.IntegrationTests.Support;

/// <summary>
/// Runs the whole BotFarm app in memory, configured as in production, against the test MongoDB server and a fake
/// Telegram Bot API. Every bot the app references is enabled, with a fake token and admin chat.
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

    private BotFarmAppFactory()
    {
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
    /// Drops every database on the test server, starts the app and waits until every bot accepts updates.
    /// </summary>
    public static async Task<BotFarmAppFactory> CreateAsync()
    {
        var databases = await (await MongoServer.Client.ListDatabaseNamesAsync()).ToListAsync();
        foreach (var database in databases.Except(ServerDatabases))
        {
            await MongoServer.Client.DropDatabaseAsync(database);
        }

        var factory = new BotFarmAppFactory();
        await factory.WaitUntilBotsAcceptUpdatesAsync();

        return factory;
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

        for (var index = 0; index < BotNames.Count; index++)
        {
            var botName = BotNames[index];
            // Each bot gets its own Telegram user ID, which is the part of the token before the colon.
            var token = $"{100_000_001 + index}:integration-test-token";
            var section = BotConfigValidator.SectionPath(botName);

            builder.UseSetting($"{section}:Enabled", "true");
            builder.UseSetting($"{section}:Token", token);
            builder.UseSetting($"{section}:AdminChatId", AdminChatId);
            builder.UseSetting($"{section}:WebhookSecret", string.Empty);
            Telegram.AddBot(botName, token);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITelegramBotClientFactory>();
            services.AddSingleton<ITelegramBotClientFactory>(Telegram);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _dataDirectory.Dispose();
    }

    private async Task WaitUntilBotsAcceptUpdatesAsync()
    {
        // Reading the services starts the app.
        var botControl = Services.GetRequiredService<IBotControlService>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            while (!BotNames.All(botControl.CanProcessUpdates))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            var statuses = string.Join(", ", botControl.GetStatuses().Select(status => $"{status.BotName}: {status.Status}"));
            Assert.Fail($"The bots did not start accepting updates within 30 seconds ({statuses}).");
        }
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)"|value="(?<token>[^"]+)"[^>]*name="__RequestVerificationToken""")]
    private static partial Regex AntiforgeryTokenInput();
}
