using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MongoDB.Driver;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using TestClients = BotFarm.TestKit.TelegramBotClientFactory;

namespace BotFarm.Core.UnitTests.Services;

/// <summary>
/// Audit logs written when bot settings are saved through <see cref="BotControlCoordinator"/>.
/// </summary>
[TestFixture]
public class BotSettingsAuditLogTests
{
    private const string BotName = "TestBot";
    private const string StoredToken = "123456:TEST-TOKEN";
    private const string NewToken = "123456:NEW-TEST-TOKEN";
    private const string OtherAccountToken = "654321:OTHER-TEST-TOKEN";
    private const string WebhookSecret = "test_audit-webhook-secret";
    private const string TelegramMessage = "Unauthorized: TELEGRAM-EXCEPTION-TEXT";
    private const string Administrator = "alice";

    private IBotSettingsStore _store = default!;
    private BotSettingsProtector _protector = default!;
    private IBotControlStateStore _stateStore = default!;
    private TelegramBotClient _verificationClient = default!;
    private IBotService _bot = default!;
    private IBotWebhookInitializer _initializer = default!;
    private FakeLogCollector _logs = default!;
    private BotControlCoordinator _sut = default!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IBotSettingsStore>();
        _store.FindBotNameByTelegramBotIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        _protector = new BotSettingsProtector(new EphemeralDataProtectionProvider());
        _store.GetAsync(BotName, Arg.Any<CancellationToken>()).Returns(new BotSettings
        {
            BotName = BotName,
            ProtectedToken = _protector.Protect(BotName, StoredToken),
            TelegramBotId = 123456,
            AdminChatId = 42,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = "admin"
        });
        _stateStore = Substitute.For<IBotControlStateStore>();
        _stateStore.GetAsync(BotName, Arg.Any<CancellationToken>()).Returns((BotControlState?)null);
        _stateStore.TryInsertAsync(Arg.Any<BotControlState>(), Arg.Any<CancellationToken>()).Returns(true);

        _logs = new FakeLogCollector();
        _verificationClient = TestClients.CreateSubstitute(NewToken);
        _verificationClient.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new Telegram.Bot.Types.BotFullInfo
            {
                Id = 123456,
                IsBot = true,
                FirstName = "Test",
                Username = "test_bot"
            });
        var clientFactory = Substitute.For<ITelegramBotClientFactory>();
        clientFactory.Create(Arg.Any<string>(), Arg.Any<TimeSpan?>()).Returns(_verificationClient);
        var verifier = new TelegramTokenVerifier(clientFactory, new FakeLogger<TelegramTokenVerifier>(_logs));

        _bot = Substitute.For<IBotService>();
        _bot.Name.Returns(BotName);
        _initializer = Substitute.For<IBotWebhookInitializer>();
        _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>()).Returns(true);

        var provider = new BotSettingsProvider(
            _store,
            _protector,
            TimeProvider.System,
            new FakeLogger<BotSettingsProvider>(_logs));
        _sut = new BotControlCoordinator(
            provider,
            _store,
            verifier,
            _stateStore,
            BotRegistryFactory.CreateSubstitute(_bot),
            _initializer,
            Substitute.For<INotificationService>(),
            new FakeLogger<BotControlCoordinator>(_logs),
            TestMetrics.BotEvents());
    }

    [Test]
    public async Task AcceptedSave_LogsTheBotTheChangedSettingNamesAndTheAdministratorAtInformation()
    {
        await InitializeAsync(enabled: false);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = NewToken,
            AdminChatId = 42,
            WebhookSecret = WebhookSecret,
            Emoji = "🤖",
            ChangedBy = Administrator
        });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
        var entry = _logs.GetSnapshot().Single(record => record.Category == typeof(BotControlCoordinator).FullName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(entry.Message, Does.Contain(BotName).And.Contain(Administrator));
            Assert.That(StateValue(entry, "ChangedSettings"), Is.EqualTo("Token, WebhookSecret, Emoji"));
        }

        AssertNoSecretInLogs();
    }

    [Test]
    public async Task AcceptedSave_OfAnAdminChatOnly_NamesOnlyTheAdminChat()
    {
        await InitializeAsync(enabled: false);

        await _sut.SaveAsync(BotName, new BotSettingsChange { AdminChatId = 7, ChangedBy = Administrator });

        var entry = _logs.GetSnapshot().Single(record => record.Category == typeof(BotControlCoordinator).FullName);
        Assert.That(StateValue(entry, "ChangedSettings"), Is.EqualTo("AdminChatId"));
    }

    [Test]
    public async Task FormatRejection_LogsTheSettingNamesTheCategoriesAndTheAdministratorAtWarning()
    {
        await InitializeAsync(enabled: false);
        const string invalidSecret = "not a valid secret!";

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = "TEST-TOKEN-WITHOUT-ID",
            WebhookSecret = invalidSecret,
            ChangedBy = Administrator
        });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Rejected>());
        var entry = _logs.GetSnapshot().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Message, Does.Contain(BotName).And.Contain(Administrator));
            Assert.That(StateValue(entry, "Settings"), Is.EqualTo("Token, WebhookSecret"));
            Assert.That(StateValue(entry, "Rejections"), Is.EqualTo("InvalidToken, InvalidWebhookSecret"));
            Assert.That(LogsContain("TEST-TOKEN-WITHOUT-ID"), Is.False);
            Assert.That(LogsContain(invalidSecret), Is.False);
        }
    }

    [Test]
    public async Task TokenRejectedByTelegram_LogsTheCategoryTheErrorCodeAndTheException()
    {
        await InitializeAsync(enabled: false);
        _verificationClient.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException(TelegramMessage, 401));

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = NewToken, ChangedBy = Administrator });

        AssertRejectedWithWarning(result, BotSettingsRejection.RejectedByTelegram);
        Assert.That(_logs.GetSnapshot().Any(record => record.Message.Contains("401", StringComparison.Ordinal)), Is.True);
        AssertExceptionLogged<ApiRequestException>();
        AssertNoSecretInLogs();
    }

    [Test]
    public async Task UnreachableTelegram_LogsTheCategoryAndTheException()
    {
        await InitializeAsync(enabled: false);
        _verificationClient.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestException(TelegramMessage, new HttpRequestException(TelegramMessage)));

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = NewToken, ChangedBy = Administrator });

        AssertRejectedWithWarning(result, BotSettingsRejection.TelegramUnreachable);
        AssertExceptionLogged<RequestException>();
        AssertNoSecretInLogs();
    }

    [Test]
    public async Task StoreFailure_LogsTheCategoryAndTheException()
    {
        await InitializeAsync(enabled: false);
        _store.UpsertAsync(Arg.Any<BotSettings>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MongoException(TelegramMessage));

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = NewToken,
            WebhookSecret = WebhookSecret,
            ChangedBy = Administrator
        });

        AssertRejectedWithWarning(result, BotSettingsRejection.StoreUnavailable);
        AssertExceptionLogged<MongoException>();
        AssertNoSecretInLogs();
    }

    [Test]
    public async Task TelegramFailureWhileReapplying_LogsTheErrorCodeAndTheException()
    {
        await InitializeAsync(enabled: true);
        _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException(TelegramMessage, 400));

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = NewToken, ChangedBy = Administrator });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
        var records = _logs.GetSnapshot();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(records.Any(record => record.Level == LogLevel.Information
                                              && StateValue(record, "ChangedSettings") == "Token"), Is.True);
            Assert.That(records.Any(record => record.Level == LogLevel.Warning
                                              && record.Message.Contains("400", StringComparison.Ordinal)), Is.True);
        }

        AssertExceptionLogged<ApiRequestException>();
        AssertNoSecretInLogs();
    }

    [Test]
    public async Task SaveWhileAnotherOperationHoldsTheBot_LogsTheBusyRejectionAtWarning()
    {
        await InitializeAsync(enabled: false);
        var verification = new TaskCompletionSource<Telegram.Bot.Types.BotFullInfo>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _verificationClient.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .Returns(verification.Task);
        var first = _sut.SaveAsync(BotName, new BotSettingsChange { Token = NewToken, ChangedBy = "bob" });

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Emoji = "🚀",
            ChangedBy = Administrator
        });
        verification.SetResult(new Telegram.Bot.Types.BotFullInfo { Id = 123456, IsBot = true, FirstName = "Test" });
        await first;

        AssertRejectedWithWarning(result, BotSettingsRejection.BotBusy, expectedSettings: "Emoji");
        AssertNoSecretInLogs();
    }

    [Test]
    public async Task AccountChangeWaitingForConfirmation_LogsBothBotIdsWithoutTheToken()
    {
        await InitializeAsync(enabled: false);
        _verificationClient.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Telegram.Bot.Types.BotFullInfo { Id = 654321, IsBot = true, FirstName = "Other" });

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = OtherAccountToken,
            ChangedBy = Administrator
        });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.NeedsAccountChangeConfirmation>());
        var entry = _logs.GetSnapshot().Single(record => record.Category == typeof(BotControlCoordinator).FullName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(entry.Message, Does.Contain(BotName).And.Contain(Administrator));
            Assert.That(entry.Message, Does.Contain("123456").And.Contain("654321"));
            Assert.That(LogsContain(OtherAccountToken), Is.False);
            Assert.That(LogsContain("OTHER-TEST-TOKEN"), Is.False);
        }
    }

    private async Task InitializeAsync(bool enabled)
    {
        if (enabled)
        {
            _stateStore.GetAsync(BotName, Arg.Any<CancellationToken>()).Returns(new BotControlState
            {
                BotName = BotName,
                DesiredEnabled = true,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await _sut.InitializeAsync();
        _logs.Clear();
    }

    private void AssertRejectedWithWarning(
        BotSettingsSaveResult result,
        BotSettingsRejection rejection,
        string expectedSettings = "Token")
    {
        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Rejected>());
        var entry = _logs.GetSnapshot().Single(record => record.Category == typeof(BotControlCoordinator).FullName
                                                          && record.Message.Contains(Administrator, StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Message, Does.Contain(BotName));
            Assert.That(StateValue(entry, "Settings"), Does.Contain(expectedSettings));
            Assert.That(StateValue(entry, "Rejections"), Is.EqualTo(rejection.ToString()));
        }
    }

    /// <summary>
    /// Checks every log entry, its structured values and any exception attached to it, without echoing a secret
    /// in the failure message.
    /// </summary>
    private void AssertNoSecretInLogs()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LogsContain(StoredToken), Is.False, "A log entry contains the stored token.");
            Assert.That(LogsContain(NewToken), Is.False, "A log entry contains the new token.");
            Assert.That(LogsContain("NEW-TEST-TOKEN"), Is.False, "A log entry contains the token's secret part.");
            Assert.That(LogsContain(WebhookSecret), Is.False, "A log entry contains the webhook secret.");
        }
    }

    /// <summary>
    /// Checks that a warning carries an exception of type <typeparamref name="TException"/>.
    /// </summary>
    private void AssertExceptionLogged<TException>()
        where TException : Exception
    {
        Assert.That(
            _logs.GetSnapshot().Any(record => record.Level == LogLevel.Warning && record.Exception?.GetType() == typeof(TException)),
            Is.True,
            $"No warning carries the {typeof(TException).Name}.");
    }

    private bool LogsContain(string value) =>
        _logs.GetSnapshot().Any(record =>
            record.Message.Contains(value, StringComparison.Ordinal)
            || (record.StructuredState ?? []).Any(pair => pair.Value?.Contains(value, StringComparison.Ordinal) == true)
            || record.Exception?.ToString().Contains(value, StringComparison.Ordinal) == true);

    private static string? StateValue(FakeLogRecord record, string key) =>
        record.StructuredState?.SingleOrDefault(pair => pair.Key == key).Value;
}
