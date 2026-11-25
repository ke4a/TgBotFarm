using System.Globalization;
using System.Net;
using System.Reflection;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using BotFarm.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace BotFarm.Core.UnitTests.Services;

/// <summary>
/// Saves bot settings through <see cref="BotControlCoordinator"/> as <see cref="IBotSettingsService"/>.
/// </summary>
[TestFixture]
public class BotControlCoordinatorSettingsTests
{
    private const string BotName = "TestBot";
    private const string Token = "123456:TEST-TOKEN";
    private const long TelegramBotId = 123456;
    private const string OtherToken = "654321:OTHER-TEST-TOKEN";
    private const long OtherTelegramBotId = 654321;
    private const string Username = "test_bot";

    private FakeSettingsProvider _settings = default!;
    private IBotSettingsStore _settingsStore = default!;
    private ITelegramTokenVerifier _verifier = default!;
    private TestStateStore _stateStore = default!;
    private IBotService _bot = default!;
    private IBotWebhookInitializer _initializer = default!;
    private BotControlCoordinator _sut = default!;
    private ServiceProvider _metricsProvider = default!;
    private MetricCollector<long> _events = default!;

    [SetUp]
    public void SetUp()
    {
        _settings = new FakeSettingsProvider();
        _settingsStore = Substitute.For<IBotSettingsStore>();
        _settingsStore.FindBotNameByTelegramBotIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _verifier = Substitute.For<ITelegramTokenVerifier>();
        _verifier.VerifyAsync(Token, Arg.Any<CancellationToken>())
            .Returns(new TelegramTokenVerification.Verified(TelegramBotId, Username));
        _verifier.VerifyAsync(OtherToken, Arg.Any<CancellationToken>())
            .Returns(new TelegramTokenVerification.Verified(OtherTelegramBotId, "other_bot"));
        _stateStore = new TestStateStore();
        _bot = Substitute.For<IBotService>();
        _bot.Name.Returns(BotName);
        _initializer = Substitute.For<IBotWebhookInitializer>();
        _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>()).Returns(true);
        _metricsProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = _metricsProvider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
        _events = new MetricCollector<long>(meterFactory, UpdateMetrics.MeterName, "botfarm.bot.events");
        _sut = new BotControlCoordinator(
            _settings,
            _settingsStore,
            _verifier,
            _stateStore,
            BotRegistryFactory.CreateSubstitute(_bot),
            _initializer,
            Substitute.For<INotificationService>(),
            Substitute.For<ILogger<BotControlCoordinator>>(),
            new BotEventMetrics(meterFactory));
    }

    [TearDown]
    public void TearDown()
    {
        _events.Dispose();
        _metricsProvider.Dispose();
    }

    [Test]
    public async Task AcceptedSave_RecordsOneSettingsChangedEvent()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Emoji = "🚀", ChangedBy = "admin" });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
        Assert.That(RecordedEvents(), Is.EqualTo(new[] { "settings_changed" }));
    }

    [Test]
    public async Task AcceptedSaveOfATokenOnAnEnabledBot_RecordsOnlyTheSettingsChangedEvent()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token, ChangedBy = "admin" });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
        Assert.That(RecordedEvents(), Is.EqualTo(new[] { "settings_changed" }));
    }

    [Test]
    public async Task SaveWithoutChanges_RecordsNoEvent()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { ChangedBy = "admin" });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
        Assert.That(RecordedEvents(), Is.Empty);
    }

    [Test]
    public async Task RejectedSave_RecordsNoEvent()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        await _sut.SaveAsync(BotName, new BotSettingsChange { Emoji = "🤖✨", ChangedBy = "admin" });

        Assert.That(RecordedEvents(), Is.Empty);
    }

    [Test]
    public async Task SaveThatCannotBeStored_RecordsNoEvent()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);
        _settings.PersistException = new MongoException("Simulated store failure.");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Emoji = "🚀", ChangedBy = "admin" });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Rejected>());
        Assert.That(RecordedEvents(), Is.Empty);
    }

    private IEnumerable<object?> RecordedEvents() =>
        _events.GetMeasurementSnapshot().Where(m => m.Value > 0).Select(m => m.Tags["event"]);

    [Test]
    public async Task FirstSave_OnANotConfiguredBot_StoresSettingsReturnsConfiguredAndLeavesTheBotDisabled()
    {
        await InitializeAsync(desiredEnabled: null);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = Token,
            AdminChatId = 42,
            ChangedBy = "admin"
        });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.View.State, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(saved.View.TelegramBotId, Is.EqualTo(TelegramBotId));
            Assert.That(saved.VerifiedTelegramUsername, Is.EqualTo(Username));
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.False);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
            Assert.That(_settings.Persisted.Single().ChangedBy, Is.EqualTo("admin"));
            Assert.That(_stateStore.States[BotName].DesiredEnabled, Is.False);
        }

        _bot.Received().ReloadClient();
        await _initializer.DidNotReceive().EnableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveThatCompletesANotConfiguredBot_AppliesItsSavedDesiredState()
    {
        await InitializeAsync(desiredEnabled: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token, AdminChatId = 42 });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
        }

        Received.InOrder(() =>
        {
            _bot.ReloadClient();
            _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task SaveThatLeavesTheBotIncomplete_KeepsItNotConfigured()
    {
        await InitializeAsync(desiredEnabled: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { AdminChatId = 42, Emoji = "🤖" });

        var saved = (BotSettingsSaveResult.Saved)result;
        Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.NotConfigured));
        await _initializer.DidNotReceive().EnableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FormatErrors_ReturnOneRejectionPerSettingAndStoreNothing()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = "not-a-token",
            AdminChatId = 0,
            WebhookSecret = "not a valid secret!"
        });

        AssertRejected(
            result,
            BotSettingsRejection.InvalidToken,
            BotSettingsRejection.InvalidAdminChat,
            BotSettingsRejection.InvalidWebhookSecret);
        await _verifier.DidNotReceive().VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EmojiOfMoreThanOneCharacter_IsRejectedAndStoresNothing()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Emoji = "🤖✨" });

        AssertRejected(result, BotSettingsRejection.InvalidEmoji);
        Assert.That(_settings.Persisted, Is.Empty);
    }

    [Test]
    public async Task MissingAdminChat_WhenNoneIsStored_IsRejected()
    {
        await InitializeAsync(desiredEnabled: false);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token });

        AssertRejected(result, BotSettingsRejection.InvalidAdminChat);
    }

    [Test]
    public async Task MissingAdminChat_WhenOneIsStored_KeepsTheStoredOne()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Emoji = "🤖" });

        var saved = (BotSettingsSaveResult.Saved)result;
        Assert.That(saved.View.AdminChatId, Is.EqualTo(42));
    }

    [Test]
    public async Task DerivedWebhookSecretCombinedWithACustomSecret_IsRejected()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            WebhookSecret = "custom-secret",
            UseDerivedWebhookSecret = true
        });

        AssertRejected(result, BotSettingsRejection.InvalidWebhookSecret);
    }

    [Test]
    public async Task TokenRejectedByTelegram_KeepsStoredSettings()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);
        _verifier.VerifyAsync(OtherToken, Arg.Any<CancellationToken>())
            .Returns(new TelegramTokenVerification.RejectedByTelegram());

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = OtherToken });

        AssertRejected(result, BotSettingsRejection.RejectedByTelegram);
        _bot.DidNotReceive().ReloadClient();
    }

    [Test]
    public async Task TokenThatCannotBeVerified_KeepsStoredSettings()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);
        _verifier.VerifyAsync(OtherToken, Arg.Any<CancellationToken>())
            .Returns(new TelegramTokenVerification.TelegramUnreachable());

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = OtherToken });

        AssertRejected(result, BotSettingsRejection.TelegramUnreachable);
    }

    [Test]
    public async Task TokenOfAnotherRegisteredBot_IsRejectedAsDuplicate()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);
        _settingsStore.FindBotNameByTelegramBotIdAsync(OtherTelegramBotId, Arg.Any<CancellationToken>())
            .Returns("OtherBot");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = OtherToken,
            ConfirmAccountChange = true
        });

        AssertRejected(result, BotSettingsRejection.DuplicateAccount);
    }

    [Test]
    public async Task TokenOfTheSameBot_IsNotADuplicate()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);
        _settingsStore.FindBotNameByTelegramBotIdAsync(TelegramBotId, Arg.Any<CancellationToken>())
            .Returns(BotName);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
    }

    [Test]
    public async Task DuplicateKeyOnWrite_IsRejectedAsDuplicate()
    {
        await InitializeAsync(desiredEnabled: false);
        _settings.PersistException = CreateWriteException(ServerErrorCategory.DuplicateKey);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token, AdminChatId = 42 });

        AssertRejected(result, BotSettingsRejection.DuplicateAccount);
        Assert.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.NotConfigured));
    }

    [Test]
    public async Task TokenOfADifferentTelegramBot_NeedsConfirmationAndSavesOnlyWhenConfirmed()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        var unconfirmed = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = OtherToken });

        Assert.That(
            unconfirmed,
            Is.EqualTo(new BotSettingsSaveResult.NeedsAccountChangeConfirmation(TelegramBotId, OtherTelegramBotId)));
        Assert.That(_settings.Persisted, Is.Empty);

        var confirmed = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = OtherToken,
            ConfirmAccountChange = true
        });

        var saved = (BotSettingsSaveResult.Saved)confirmed;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.View.TelegramBotId, Is.EqualTo(OtherTelegramBotId));
            Assert.That(saved.VerifiedTelegramUsername, Is.EqualTo("other_bot"));
            Assert.That(_settings.Persisted, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task AdminChatAndEmojiChanges_OnAnEnabledBot_DoNotReapply()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { AdminChatId = 43, Emoji = "🤖" });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.View.AdminChatId, Is.EqualTo(43));
            Assert.That(saved.View.Emoji, Is.EqualTo("🤖"));
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.VerifiedTelegramUsername, Is.Null);
        }

        await _initializer.DidNotReceiveWithAnyArgs().EnableAsync(default!, default);
        await _initializer.DidNotReceiveWithAnyArgs().DisableAsync(default!, default);
        _bot.DidNotReceive().ReloadClient();
    }

    [Test]
    public async Task TokenChange_OnAnEnabledBot_DeletesTheOldWebhookReloadsTheClientAndReapplies()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = OtherToken,
            ConfirmAccountChange = true
        });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
        }

        Received.InOrder(() =>
        {
            _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>());
            _bot.ReloadClient();
            _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task TokenChange_OnAnEnabledBot_IgnoresAFailedOldWebhookDeletion()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);
        _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Unauthorized"));

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token });

        var saved = (BotSettingsSaveResult.Saved)result;
        Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
        _bot.Received(1).ReloadClient();
        await _initializer.Received(1).EnableAsync(_bot, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TokenChange_OnADisabledBot_OnlyReloadsTheClient()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.False);
        }

        _bot.Received(1).ReloadClient();
        await _initializer.DidNotReceiveWithAnyArgs().EnableAsync(default!, default);
        await _initializer.DidNotReceiveWithAnyArgs().DisableAsync(default!, default);
    }

    [Test]
    public async Task SettingAWebhookSecret_OnAnEnabledBot_Reapplies()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { WebhookSecret = "custom-secret" });

        var saved = (BotSettingsSaveResult.Saved)result;
        Assert.That(saved.View.HasCustomWebhookSecret, Is.True);
        await _initializer.Received(1).EnableAsync(_bot, Arg.Any<CancellationToken>());
        await _initializer.DidNotReceiveWithAnyArgs().DisableAsync(default!, default);
    }

    [Test]
    public async Task ClearingTheWebhookSecret_OnAnEnabledBot_Reapplies()
    {
        await InitializeAsync(desiredEnabled: true, configured: true, customWebhookSecret: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { UseDerivedWebhookSecret = true });

        var saved = (BotSettingsSaveResult.Saved)result;
        Assert.That(saved.View.HasCustomWebhookSecret, Is.False);
        await _initializer.Received(1).EnableAsync(_bot, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UsingTheDerivedSecret_WhenNoCustomSecretIsStored_DoesNotReapply()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);

        await _sut.SaveAsync(BotName, new BotSettingsChange { UseDerivedWebhookSecret = true });

        await _initializer.DidNotReceiveWithAnyArgs().EnableAsync(default!, default);
    }

    [Test]
    public async Task ReapplyChanges_WhileTheBotStateIsUnknown_AreBusy()
    {
        _stateStore.FailReads = true;
        await InitializeAsync(desiredEnabled: true, configured: true);
        Assume.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Unknown));

        var token = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token });
        var secret = await _sut.SaveAsync(BotName, new BotSettingsChange { WebhookSecret = "custom-secret" });
        var adminChat = await _sut.SaveAsync(BotName, new BotSettingsChange { AdminChatId = 43 });

        AssertRejected(token, BotSettingsRejection.BotBusy);
        AssertRejected(secret, BotSettingsRejection.BotBusy);
        Assert.That(adminChat, Is.TypeOf<BotSettingsSaveResult.Saved>());
        Assert.That(_settings.Persisted, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ReapplyChanges_WhileTheBotIsPending_AreBusy()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);
        // Leaves every bot pending without holding its lock.
        await _sut.PauseAllForShutdownAsync();
        Assume.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Pending));

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token });

        AssertRejected(result, BotSettingsRejection.BotBusy);
    }

    [Test]
    public async Task TokenChange_OnABotThatFailedToTurnOn_IsStoredAndTurnsItOnWithTheNewClient()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);
        _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Unauthorized", 401));
        await _sut.SetEnabledAsync(BotName, true);
        Assume.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Error));
        _initializer.ClearReceivedCalls();
        _bot.ClearReceivedCalls();
        _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = OtherToken, ConfirmAccountChange = true });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
            Assert.That(_settings.Persisted.Single().Token, Is.EqualTo(OtherToken));
        }

        Received.InOrder(() =>
        {
            _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>());
            _bot.ReloadClient();
            _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task WebhookSecretChange_OnABotThatFailedToTurnOff_KeepsItOff()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);
        _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network"));
        await _sut.SetEnabledAsync(BotName, false);
        Assume.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Error));
        _initializer.ClearReceivedCalls();
        _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { WebhookSecret = "custom-secret" });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.False);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }

        await _initializer.DidNotReceive().EnableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FailedWrite_IsStoreUnavailable()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);
        _settings.PersistException = new MongoException("write failed");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { AdminChatId = 43 });

        AssertRejected(result, BotSettingsRejection.StoreUnavailable);
    }

    [Test]
    public async Task SaveAfterAFailedWrite_IsStoredWithoutReadingTheSettingsAgain()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);
        _settings.PersistException = new MongoException("write failed");
        await _sut.SaveAsync(BotName, new BotSettingsChange { AdminChatId = 43 });
        _settings.PersistException = null;
        var loadsBefore = _settings.LoadCount;

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { AdminChatId = 44 });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
            Assert.That(_settings.LoadCount, Is.EqualTo(loadsBefore));
        }
    }

    [Test]
    public async Task TokenChange_WhenDeletingTheOldWebhookTimesOut_IsSavedAndApplied()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);
        _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestException("Request timed out", new TaskCanceledException()));

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
        }
    }

    [Test]
    public async Task TokenChange_WhenReapplyingTimesOut_IsSavedWithTheBotInError()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);
        _initializer.DisableAsync(_bot, Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestException("Request timed out", new TaskCanceledException()));
        _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestException("Request timed out", new TaskCanceledException()));

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token });

        AssertSavedWithBotInError(result);
    }

    [Test]
    public async Task SettingsChange_WhenReapplyingFailsUnexpectedly_IsSavedWithTheBotInError()
    {
        await InitializeAsync(desiredEnabled: true, configured: true);
        _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>())
            .ThrowsAsync(new NotSupportedException());

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { WebhookSecret = "custom-secret" });

        AssertSavedWithBotInError(result);
        Assert.That(_settings.Persisted, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SaveWhenSettingsCouldNotBeRead_ReloadsThemFirst()
    {
        _settings.LoadFails = true;
        await InitializeAsync(desiredEnabled: false, configured: true);
        _settings.LoadFails = false;

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Emoji = "🤖" });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
        Assert.That(_settings.LoadCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SaveWhenSettingsStillCannotBeRead_IsStoreUnavailableAndWritesNothing()
    {
        _settings.LoadFails = true;
        await InitializeAsync(desiredEnabled: false, configured: true);

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { AdminChatId = 43 });

        AssertRejected(result, BotSettingsRejection.StoreUnavailable);
        Assert.That(_settings.Persisted, Is.Empty);
    }

    [Test]
    public async Task SaveWhileAnotherOperationHoldsTheBot_IsBusy()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenance = _sut.RunWithUpdatesPausedAsync(BotName, async _ =>
        {
            started.SetResult();
            await release.Task;
            return true;
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Emoji = "🤖" });

        release.SetResult();
        await maintenance;
        AssertRejected(result, BotSettingsRejection.BotBusy);
        Assert.That(_settings.Persisted, Is.Empty);
    }

    [Test]
    public async Task GetView_NamesTheRunningAccountOnlyWhenItHasTheStoredBotId()
    {
        await InitializeAsync(desiredEnabled: false, configured: true);

        _bot.Me.Returns((User?)null);
        var beforeEnable = _sut.GetView(BotName);
        _bot.Me.Returns(new User { Id = TelegramBotId, Username = Username });
        var matching = _sut.GetView(BotName);
        _bot.Me.Returns(new User { Id = OtherTelegramBotId, Username = "other_bot" });
        var otherAccount = _sut.GetView(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(beforeEnable.TelegramUsername, Is.Null.Or.Empty);
            Assert.That(matching.TelegramUsername, Is.EqualTo(Username));
            Assert.That(otherAccount.TelegramUsername, Is.Null.Or.Empty);
        }
    }

    [Test]
    public async Task UnreadableBot_IsNotConfiguredWithItsSavedDesiredStateAndCannotBeEnabled()
    {
        await InitializeUnreadableAsync(desiredEnabled: true, "Token");

        var status = _sut.GetStatuses().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.Status, Is.EqualTo(BotRuntimeState.NotConfigured));
            Assert.That(status.DesiredEnabled, Is.True);
            Assert.That(status.AppliedEnabled, Is.False);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }

        await Assert.ThrowsAsync<BotNotConfiguredException>(() => _sut.SetEnabledAsync(BotName, true));
        await _initializer.DidNotReceive().EnableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnreadableToken_SaveWithoutANewToken_IsRejectedAndStoresNothing()
    {
        await InitializeUnreadableAsync(desiredEnabled: true, "Token");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { AdminChatId = 7, ChangedBy = "admin" });

        AssertRejected(result, BotSettingsRejection.InvalidToken);
        Assert.That(_settings.Persisted, Is.Empty);
    }

    [Test]
    public async Task UnreadableToken_SaveWithANewToken_RestoresTheBotAndAppliesItsSavedDesiredState()
    {
        await InitializeUnreadableAsync(desiredEnabled: true, "Token");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token, ChangedBy = "admin" });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.View.State, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(saved.View.UnreadableSettings, Is.Empty);
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
        }

        Received.InOrder(() =>
        {
            _bot.ReloadClient();
            _initializer.EnableAsync(_bot, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task UnreadableToken_SaveWithATokenOfADifferentTelegramBot_NeedsConfirmationAndSavesWhenConfirmed()
    {
        await InitializeUnreadableAsync(desiredEnabled: true, "Token");

        var unconfirmed = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = OtherToken,
            ChangedBy = "admin"
        });

        Assert.That(
            unconfirmed,
            Is.EqualTo(new BotSettingsSaveResult.NeedsAccountChangeConfirmation(TelegramBotId, OtherTelegramBotId)));
        Assert.That(_settings.Persisted, Is.Empty);

        var confirmed = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = OtherToken,
            ConfirmAccountChange = true,
            ChangedBy = "admin"
        });

        Assert.That(confirmed, Is.TypeOf<BotSettingsSaveResult.Saved>());
        var saved = (BotSettingsSaveResult.Saved)confirmed;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.View.State, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(saved.View.TelegramBotId, Is.EqualTo(OtherTelegramBotId));
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(_settings.Persisted, Has.Count.EqualTo(1));
            Assert.That(_settings.Persisted[0].ConfirmAccountChange, Is.True);
        }
    }

    [Test]
    public async Task UnreadableToken_SaveWithANewToken_KeepsABotWhoseSavedStateIsDisabledDisabled()
    {
        await InitializeUnreadableAsync(desiredEnabled: false, "Token");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token, ChangedBy = "admin" });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.False);
        }

        await _initializer.DidNotReceive().EnableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnreadableTokenAndWebhookSecret_SaveWithOnlyANewToken_IsRejected()
    {
        await InitializeUnreadableAsync(desiredEnabled: true, "Token", "WebhookSecret");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Token = Token, ChangedBy = "admin" });

        AssertRejected(result, BotSettingsRejection.InvalidWebhookSecret);
        Assert.That(_settings.Persisted, Is.Empty);
        await _verifier.DidNotReceive().VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnreadableTokenAndWebhookSecret_SaveWithNeither_RejectsBoth()
    {
        await InitializeUnreadableAsync(desiredEnabled: true, "Token", "WebhookSecret");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange { Emoji = "🤖", ChangedBy = "admin" });

        AssertRejected(result, BotSettingsRejection.InvalidToken, BotSettingsRejection.InvalidWebhookSecret);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnreadableTokenAndWebhookSecret_SaveWithANewTokenAndANewOrClearedSecret_RestoresTheBot(
        bool clearSecret)
    {
        await InitializeUnreadableAsync(desiredEnabled: true, "Token", "WebhookSecret");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            Token = Token,
            WebhookSecret = clearSecret ? null : "new_test-secret",
            UseDerivedWebhookSecret = clearSecret,
            ChangedBy = "admin"
        });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.View.State, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(saved.View.HasCustomWebhookSecret, Is.EqualTo(!clearSecret));
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.True);
        }
    }

    [Test]
    public async Task UnreadableWebhookSecret_SaveWithANewSecretOnly_RestoresTheBot()
    {
        await InitializeUnreadableAsync(desiredEnabled: true, "WebhookSecret");

        var result = await _sut.SaveAsync(BotName, new BotSettingsChange
        {
            WebhookSecret = "new_test-secret",
            ChangedBy = "admin"
        });

        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.View.State, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.True);
        }

        await _verifier.DidNotReceive().VerifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void SaveForAnUnregisteredBot_Throws()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _sut.SaveAsync("Unknown", new BotSettingsChange()));
    }

    /// <summary>
    /// Seeds the bot's settings and control state, then initializes the coordinator and forgets the calls it made.
    /// </summary>
    /// <param name="desiredEnabled">The stored desired state, or null for none.</param>
    /// <param name="configured">Whether a token and admin chat are stored.</param>
    /// <param name="customWebhookSecret">Whether a custom webhook secret is stored.</param>
    private async Task InitializeAsync(bool? desiredEnabled, bool configured = false, bool customWebhookSecret = false)
    {
        if (configured)
        {
            _settings.Stored = new BotSettingsView
            {
                BotName = BotName,
                State = BotSettingsState.Configured,
                HasToken = true,
                TelegramBotId = TelegramBotId,
                AdminChatId = 42,
                HasCustomWebhookSecret = customWebhookSecret
            };
        }

        if (desiredEnabled is { } enabled)
        {
            _stateStore.States[BotName] = new BotControlState
            {
                BotName = BotName,
                DesiredEnabled = enabled,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        await _sut.InitializeAsync();
        _bot.ClearReceivedCalls();
        _initializer.ClearReceivedCalls();
        _verifier.ClearReceivedCalls();
    }

    /// <summary>
    /// Seeds settings with a stored token and admin chat whose named secrets could not be unprotected, then
    /// initializes the coordinator and forgets the calls it made.
    /// </summary>
    private async Task InitializeUnreadableAsync(bool? desiredEnabled, params string[] unreadableSettings)
    {
        _settings.Stored = new BotSettingsView
        {
            BotName = BotName,
            State = BotSettingsState.Unreadable,
            HasToken = true,
            TelegramBotId = TelegramBotId,
            AdminChatId = 42,
            HasCustomWebhookSecret = unreadableSettings.Contains("WebhookSecret"),
            UnreadableSettings = unreadableSettings
        };

        await InitializeAsync(desiredEnabled);
    }

    private void AssertSavedWithBotInError(BotSettingsSaveResult result)
    {
        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Error));
            Assert.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Error));
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }
    }

    private static void AssertRejected(BotSettingsSaveResult result, params BotSettingsRejection[] rejections)
    {
        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Rejected>());
        Assert.That(((BotSettingsSaveResult.Rejected)result).Rejections, Is.EqualTo(rejections));
    }

    private static MongoWriteException CreateWriteException(ServerErrorCategory category)
    {
        // WriteError has no public constructor, so build one the way the driver does.
        var writeError = (WriteError)Activator.CreateInstance(
            typeof(WriteError),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [category, 11000, "write failed", new BsonDocument()],
            culture: null)!;
        var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

        return new MongoWriteException(connectionId, writeError, null, null);
    }

    /// <summary>
    /// Keeps one bot's settings view in memory and applies persisted changes to it the way the real provider does.
    /// </summary>
    private sealed class FakeSettingsProvider : IBotSettingsProvider
    {
        private bool _loaded;

        public BotSettingsView Stored { get; set; } = new() { BotName = BotName, State = BotSettingsState.Incomplete };
        public bool LoadFails { get; set; }
        public int LoadCount { get; private set; }
        public Exception? PersistException { get; set; }
        public List<BotSettingsChange> Persisted { get; } = [];

        public Task<BotSettingsState> LoadAsync(string botName, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            _loaded = !LoadFails;
            return Task.FromResult(GetState(botName));
        }

        public BotSettingsState GetState(string botName) => _loaded ? Stored.State : BotSettingsState.Unknown;

        public BotSettingsView GetView(string botName) =>
            _loaded ? Stored : new BotSettingsView { BotName = botName, State = BotSettingsState.Unknown };

        public Task<BotSettingsState> PersistAsync(
            string botName,
            BotSettingsChange change,
            CancellationToken cancellationToken = default)
        {
            if (!_loaded)
            {
                throw new InvalidOperationException("The settings are not loaded.");
            }

            if (PersistException is not null)
            {
                throw PersistException;
            }

            Persisted.Add(change);
            var unreadable = Stored.UnreadableSettings
                .Where(name => !(name == "Token" && !string.IsNullOrEmpty(change.Token)))
                .Where(name => !(name == "WebhookSecret"
                                 && (!string.IsNullOrEmpty(change.WebhookSecret) || change.UseDerivedWebhookSecret)))
                .ToArray();
            var hasToken = Stored.HasToken || !string.IsNullOrEmpty(change.Token);
            var adminChatId = change.AdminChatId ?? Stored.AdminChatId;
            Stored = Stored with
            {
                HasToken = hasToken,
                TelegramBotId = string.IsNullOrEmpty(change.Token)
                    ? Stored.TelegramBotId
                    : long.Parse(change.Token[..change.Token.IndexOf(':')], CultureInfo.InvariantCulture),
                AdminChatId = adminChatId,
                HasCustomWebhookSecret = !string.IsNullOrEmpty(change.WebhookSecret)
                                         || (!change.UseDerivedWebhookSecret && Stored.HasCustomWebhookSecret),
                Emoji = string.IsNullOrEmpty(change.Emoji) ? null : change.Emoji,
                UpdatedBy = change.ChangedBy,
                UnreadableSettings = unreadable,
                State = unreadable.Length > 0
                    ? BotSettingsState.Unreadable
                    : hasToken && adminChatId is not null and not 0
                        ? BotSettingsState.Configured
                        : BotSettingsState.Incomplete
            };

            return Task.FromResult(Stored.State);
        }

    }

    private sealed class TestStateStore : IBotControlStateStore
    {
        public Dictionary<string, BotControlState> States { get; } = new(StringComparer.Ordinal);
        public bool FailReads { get; set; }

        public Task<BotControlState?> GetAsync(string botName, CancellationToken cancellationToken = default)
        {
            if (FailReads)
            {
                throw new MongoException("Simulated state-store read failure.");
            }

            States.TryGetValue(botName, out var state);
            return Task.FromResult(state);
        }

        public Task<bool> TryInsertAsync(BotControlState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(States.TryAdd(state.BotName, state));

        public Task<BotControlState?> TrySetDesiredStateAsync(
            string botName,
            bool desiredEnabled,
            string commandId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (!States.TryGetValue(botName, out var state))
            {
                return Task.FromResult<BotControlState?>(null);
            }

            States[botName] = state with
            {
                DesiredEnabled = desiredEnabled,
                LastCommandId = commandId,
                UpdatedAtUtc = updatedAtUtc
            };
            return Task.FromResult<BotControlState?>(States[botName]);
        }
    }
}
