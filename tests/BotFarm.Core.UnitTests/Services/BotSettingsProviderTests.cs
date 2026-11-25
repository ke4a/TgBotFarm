using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotSettingsProviderTests
{
    private const string BotName = "TestBot";
    private const string Token = "123456:TEST-TOKEN";
    private const string WebhookSecret = "test_webhook-secret";

    private IBotSettingsStore _store = null!;
    private BotSettingsProtector _protector = null!;
    private ServiceProvider _services = null!;
    private BotSettingsProvider _provider = null!;
    private IOptionsMonitor<BotConfig> _botConfigs = null!;
    private ManualTimeProvider _clock = null!;
    private FakeLogCollector _logs = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IBotSettingsStore>();
        _clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 20, 9, 15, 30, 123, TimeSpan.Zero).AddTicks(4567));
        _protector = new BotSettingsProtector(new EphemeralDataProtectionProvider());
        _logs = new FakeLogCollector();
        _services = new ServiceCollection()
            .AddSingleton(_store)
            .AddSingleton(_protector)
            .AddSingleton<TimeProvider>(_clock)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<ILogger<BotSettingsProvider>>(new FakeLogger<BotSettingsProvider>(_logs))
            .AddSingleton<BotSettingsProvider>()
            .AddSingleton<IConfigureOptions<BotConfig>, BotSettingsOptionsSetup>()
            .AddBotConfig(BotName)
            .BuildServiceProvider();
        _provider = _services.GetRequiredService<BotSettingsProvider>();
        _botConfigs = _services.GetRequiredService<IOptionsMonitor<BotConfig>>();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
    }

    [Test]
    public void GetState_BeforeLoad_IsUnknown()
    {
        Assert.That(_provider.GetState(BotName), Is.EqualTo(BotSettingsState.Unknown));
    }

    [Test]
    public async Task LoadAsync_WithoutDocument_GivesIncompleteAndEmptyOptions()
    {
        var state = await _provider.LoadAsync(BotName);

        var config = _botConfigs.Get(BotName);
        var view = _provider.GetView(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(state, Is.EqualTo(BotSettingsState.Incomplete));
            Assert.That(_provider.GetState(BotName), Is.EqualTo(BotSettingsState.Incomplete));
            Assert.That(config.Token, Is.Empty);
            Assert.That(config.AdminChatId, Is.Zero);
            Assert.That(view.State, Is.EqualTo(BotSettingsState.Incomplete));
            Assert.That(view.HasToken, Is.False);
            Assert.That(view.UpdatedBy, Is.Null);
        }
    }

    [Test]
    public async Task LoadAsync_WithCompleteDocument_GivesConfiguredAndDecryptedOptions()
    {
        StoreDocument(adminChatId: 42, webhookSecret: WebhookSecret, emoji: "🤖");

        var state = await _provider.LoadAsync(BotName);

        var config = _botConfigs.Get(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(state, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(config.Token == Token, Is.True);
            Assert.That(config.WebhookSecret == WebhookSecret, Is.True);
            Assert.That(config.AdminChatId, Is.EqualTo(42));
            Assert.That(config.Emoji, Is.EqualTo("🤖"));
        }
    }

    [Test]
    public async Task LoadAsync_WithoutAdminChat_GivesIncompleteButKeepsTheToken()
    {
        StoreDocument(adminChatId: null);

        var state = await _provider.LoadAsync(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state, Is.EqualTo(BotSettingsState.Incomplete));
            Assert.That(_botConfigs.Get(BotName).Token == Token, Is.True);
        }
    }

    [Test]
    public async Task GetView_DescribesTheStoredSettingsWithoutSecrets()
    {
        var updatedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        StoreDocument(adminChatId: 42, webhookSecret: WebhookSecret, emoji: "🤖", updatedAt: updatedAt);
        await _provider.LoadAsync(BotName);

        var view = _provider.GetView(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(view.BotName, Is.EqualTo(BotName));
            Assert.That(view.State, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(view.HasToken, Is.True);
            Assert.That(view.TelegramBotId, Is.EqualTo(123456));
            Assert.That(view.TelegramUsername, Is.Null);
            Assert.That(view.AdminChatId, Is.EqualTo(42));
            Assert.That(view.Emoji, Is.EqualTo("🤖"));
            Assert.That(view.HasCustomWebhookSecret, Is.True);
            Assert.That(view.UpdatedAtUtc, Is.EqualTo(updatedAt));
            Assert.That(view.UpdatedBy, Is.EqualTo("admin"));
            Assert.That(view.UnreadableSettings, Is.Empty);
            Assert.That(view.ToString().Contains(Token, StringComparison.Ordinal), Is.False);
            Assert.That(view.ToString().Contains(WebhookSecret, StringComparison.Ordinal), Is.False);
        }
    }

    [Test]
    public async Task LoadAsync_WhenTheStoreFails_GivesUnknownAndLeavesOptionsEmpty()
    {
        _store.GetAsync(BotName, Arg.Any<CancellationToken>()).ThrowsAsync(new MongoException("Simulated failure."));

        var state = await _provider.LoadAsync(BotName);

        var config = _botConfigs.Get(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(state, Is.EqualTo(BotSettingsState.Unknown));
            Assert.That(_provider.GetState(BotName), Is.EqualTo(BotSettingsState.Unknown));
            Assert.That(_provider.GetView(BotName).State, Is.EqualTo(BotSettingsState.Unknown));
            Assert.That(config.Token, Is.Empty);
            Assert.That(config.AdminChatId, Is.Zero);
        }
    }

    [Test]
    public async Task LoadAsync_AfterOptionsWereRead_FiresTheChangeToken()
    {
        Assert.That(_botConfigs.Get(BotName).Token, Is.Empty);
        StoreDocument(adminChatId: 42);
        var changed = new List<string?>();
        using var subscription = _botConfigs.OnChange((_, name) => changed.Add(name));

        await _provider.LoadAsync(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed, Is.EqualTo(new[] { BotName }));
            Assert.That(_botConfigs.Get(BotName).Token == Token, Is.True);
        }
    }

    [Test]
    public async Task PersistAsync_BeforeLoad_ThrowsAndWritesNothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.PersistAsync(BotName, new BotSettingsChange
        {
            Token = Token,
            AdminChatId = 42,
            ChangedBy = "admin"
        }));

        await _store.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Test]
    public async Task PersistAsync_StoresProtectedValuesUpdatesTheSnapshotAndFiresTheChangeToken()
    {
        await _provider.LoadAsync(BotName);
        Assert.That(_botConfigs.Get(BotName).Token, Is.Empty);
        BotSettings? written = null;
        await _store.UpsertAsync(Arg.Do<BotSettings>(settings => written = settings), Arg.Any<CancellationToken>());
        var changed = 0;
        using var subscription = _botConfigs.OnChange((_, _) => changed++);

        var state = await _provider.PersistAsync(BotName, new BotSettingsChange
        {
            Token = Token,
            AdminChatId = 42,
            WebhookSecret = WebhookSecret,
            Emoji = "🤖",
            ChangedBy = "admin"
        });

        var config = _botConfigs.Get(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(state, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(config.Token == Token, Is.True);
            Assert.That(config.WebhookSecret == WebhookSecret, Is.True);
            Assert.That(config.AdminChatId, Is.EqualTo(42));
            Assert.That(config.Emoji, Is.EqualTo("🤖"));
            Assert.That(written, Is.Not.Null);
            Assert.That(written!.BotName, Is.EqualTo(BotName));
            Assert.That(written.TelegramBotId, Is.EqualTo(123456));
            Assert.That(written.UpdatedBy, Is.EqualTo("admin"));
            Assert.That(written.ProtectedToken!.Contains(Token, StringComparison.Ordinal), Is.False);
            Assert.That(_protector.Unprotect(BotName, written.ProtectedToken) == Token, Is.True);
            Assert.That(_protector.Unprotect(BotName, written.ProtectedWebhookSecret!) == WebhookSecret, Is.True);
            Assert.That(_provider.GetView(BotName).UpdatedBy, Is.EqualTo("admin"));
        }
    }

    [Test]
    public async Task PersistAsync_EverySave_StoresTheAdministratorAndTheTimeAndShowsThemInTheView()
    {
        await _provider.LoadAsync(BotName);
        var written = new List<BotSettings>();
        await _store.UpsertAsync(Arg.Do<BotSettings>(written.Add), Arg.Any<CancellationToken>());

        await _provider.PersistAsync(BotName, new BotSettingsChange { Token = Token, AdminChatId = 42, ChangedBy = "alice" });
        _clock.Advance(TimeSpan.FromMinutes(5));
        await _provider.PersistAsync(BotName, new BotSettingsChange { Emoji = "🤖", ChangedBy = "bob" });

        var firstTime = new DateTime(2026, 11, 20, 9, 15, 30, 123, DateTimeKind.Utc);
        var secondTime = firstTime.AddMinutes(5);
        var view = _provider.GetView(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(written.Select(settings => settings.UpdatedBy), Is.EqualTo(new[] { "alice", "bob" }));
            Assert.That(written.Select(settings => settings.UpdatedAtUtc), Is.EqualTo(new[] { firstTime, secondTime }));
            Assert.That(written[1].UpdatedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(view.UpdatedBy, Is.EqualTo("bob"));
            Assert.That(view.UpdatedAtUtc, Is.EqualTo(secondTime));
        }
    }

    [Test]
    public async Task PersistAsync_EmptyValues_KeepTheStoredTokenSecretAndAdminChatAndClearTheEmoji()
    {
        var stored = StoreDocument(adminChatId: 42, webhookSecret: WebhookSecret, emoji: "🤖");
        await _provider.LoadAsync(BotName);
        BotSettings? written = null;
        await _store.UpsertAsync(Arg.Do<BotSettings>(settings => written = settings), Arg.Any<CancellationToken>());

        await _provider.PersistAsync(BotName, new BotSettingsChange { Token = "", ChangedBy = "admin" });

        var config = _botConfigs.Get(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(written!.ProtectedToken, Is.EqualTo(stored.ProtectedToken));
            Assert.That(written.ProtectedWebhookSecret, Is.EqualTo(stored.ProtectedWebhookSecret));
            Assert.That(written.AdminChatId, Is.EqualTo(42));
            Assert.That(written.Emoji, Is.Null);
            Assert.That(config.Token == Token, Is.True);
            Assert.That(config.WebhookSecret == WebhookSecret, Is.True);
            Assert.That(config.Emoji, Is.Null);
        }
    }

    [Test]
    public async Task PersistAsync_UseDerivedWebhookSecret_RemovesTheCustomSecret()
    {
        StoreDocument(adminChatId: 42, webhookSecret: WebhookSecret);
        await _provider.LoadAsync(BotName);

        await _provider.PersistAsync(BotName, new BotSettingsChange
        {
            UseDerivedWebhookSecret = true,
            ChangedBy = "admin"
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_botConfigs.Get(BotName).WebhookSecret, Is.Null);
            Assert.That(_provider.GetView(BotName).HasCustomWebhookSecret, Is.False);
        }
    }

    [Test]
    public async Task PersistAsync_WhenTheWriteFails_KeepsTheCurrentSettings()
    {
        StoreDocument(adminChatId: 42);
        await _provider.LoadAsync(BotName);
        _store.UpsertAsync(Arg.Any<BotSettings>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MongoException("Simulated failure."));

        await Assert.ThrowsAsync<MongoException>(() => _provider.PersistAsync(BotName, new BotSettingsChange
        {
            AdminChatId = 7,
            ChangedBy = "admin"
        }));

        Assert.That(_botConfigs.Get(BotName).AdminChatId, Is.EqualTo(42));
    }

    [Test]
    public async Task LoadAsync_WithASecretProtectedByAnotherKeyRing_GivesUnreadableAndLeavesItOut()
    {
        var otherKeyRing = new BotSettingsProtector(new EphemeralDataProtectionProvider());
        _store.GetAsync(BotName, Arg.Any<CancellationToken>()).Returns(new BotSettings
        {
            BotName = BotName,
            ProtectedToken = otherKeyRing.Protect(BotName, Token),
            TelegramBotId = 123456,
            AdminChatId = 42,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = "admin"
        });

        var state = await _provider.LoadAsync(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state, Is.EqualTo(BotSettingsState.Unreadable));
            Assert.That(_botConfigs.Get(BotName).Token, Is.Empty);
            Assert.That(_provider.GetView(BotName).UnreadableSettings, Is.EqualTo(new[] { "Token" }));
        }
    }

    [Test]
    public async Task LoadAsync_WithAnUnreadableWebhookSecret_KeepsTheReadableTokenOutOfUseAndTheOtherFieldsInTheView()
    {
        var otherKeyRing = new BotSettingsProtector(new EphemeralDataProtectionProvider());
        var stored = StoreDocument(adminChatId: 42, emoji: "🤖") with
        {
            ProtectedWebhookSecret = otherKeyRing.Protect(BotName, WebhookSecret)
        };
        _store.GetAsync(BotName, Arg.Any<CancellationToken>()).Returns(stored);

        var state = await _provider.LoadAsync(BotName);

        var view = _provider.GetView(BotName);
        var config = _botConfigs.Get(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(state, Is.EqualTo(BotSettingsState.Unreadable));
            Assert.That(view.UnreadableSettings, Is.EqualTo(new[] { "WebhookSecret" }));
            Assert.That(view.HasToken, Is.True);
            Assert.That(view.HasCustomWebhookSecret, Is.True);
            Assert.That(view.TelegramBotId, Is.EqualTo(123456));
            Assert.That(view.AdminChatId, Is.EqualTo(42));
            Assert.That(view.Emoji, Is.EqualTo("🤖"));
            Assert.That(view.UpdatedBy, Is.EqualTo("admin"));
            Assert.That(config.WebhookSecret, Is.Null);
        }
    }

    [Test]
    public async Task LoadAsync_WithUnreadableSecrets_AffectsOnlyThatBot()
    {
        const string otherBotName = "OtherBot";
        var otherKeyRing = new BotSettingsProtector(new EphemeralDataProtectionProvider());
        _store.GetAsync(BotName, Arg.Any<CancellationToken>()).Returns(new BotSettings
        {
            BotName = BotName,
            ProtectedToken = otherKeyRing.Protect(BotName, Token),
            TelegramBotId = 123456,
            AdminChatId = 42,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = "admin"
        });
        _store.GetAsync(otherBotName, Arg.Any<CancellationToken>()).Returns(new BotSettings
        {
            BotName = otherBotName,
            ProtectedToken = _protector.Protect(otherBotName, "654321:TEST-OTHER-TOKEN"),
            TelegramBotId = 654321,
            AdminChatId = 7,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = "admin"
        });

        var state = await _provider.LoadAsync(BotName);
        var otherState = await _provider.LoadAsync(otherBotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state, Is.EqualTo(BotSettingsState.Unreadable));
            Assert.That(otherState, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(_provider.GetView(otherBotName).UnreadableSettings, Is.Empty);
        }
    }

    [Test]
    public async Task LoadAsync_WithUnreadableSecrets_LogsOneErrorNamingTheSettingsWithoutValues()
    {
        var otherKeyRing = new BotSettingsProtector(new EphemeralDataProtectionProvider());
        var protectedToken = otherKeyRing.Protect(BotName, Token);
        var protectedWebhookSecret = otherKeyRing.Protect(BotName, WebhookSecret);
        _store.GetAsync(BotName, Arg.Any<CancellationToken>()).Returns(new BotSettings
        {
            BotName = BotName,
            ProtectedToken = protectedToken,
            TelegramBotId = 123456,
            AdminChatId = 42,
            ProtectedWebhookSecret = protectedWebhookSecret,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = "admin"
        });

        await _provider.LoadAsync(BotName);

        var errors = _logs.GetSnapshot().Where(entry => entry.Level == LogLevel.Error).ToList();
        Assert.That(errors, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(errors[0].Message, Does.Contain(BotName));
            Assert.That(errors[0].Message, Does.Contain("Token, WebhookSecret"));
            Assert.That(errors[0].Exception, Is.Null);
            Assert.That(
                _logs.GetSnapshot().Any(entry => entry.Message.Contains(Token, StringComparison.Ordinal)
                                                 || entry.Message.Contains(WebhookSecret, StringComparison.Ordinal)
                                                 || entry.Message.Contains(protectedToken, StringComparison.Ordinal)
                                                 || entry.Message.Contains(protectedWebhookSecret, StringComparison.Ordinal)),
                Is.False);
        }
    }

    [Test]
    public async Task LoadAsync_WithReadableSecrets_LogsNoError()
    {
        StoreDocument(adminChatId: 42, webhookSecret: WebhookSecret);

        await _provider.LoadAsync(BotName);

        Assert.That(_logs.GetSnapshot().Any(entry => entry.Level == LogLevel.Error), Is.False);
    }

    private BotSettings StoreDocument(
        long? adminChatId,
        string? webhookSecret = null,
        string? emoji = null,
        DateTime? updatedAt = null)
    {
        var settings = new BotSettings
        {
            BotName = BotName,
            ProtectedToken = _protector.Protect(BotName, Token),
            TelegramBotId = 123456,
            AdminChatId = adminChatId,
            ProtectedWebhookSecret = webhookSecret is null ? null : _protector.Protect(BotName, webhookSecret),
            Emoji = emoji,
            UpdatedAtUtc = updatedAt ?? DateTime.UtcNow,
            UpdatedBy = "admin"
        };
        _store.GetAsync(BotName, Arg.Any<CancellationToken>()).Returns(settings);
        return settings;
    }
}
