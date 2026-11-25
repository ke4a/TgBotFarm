using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotControlCoordinatorInitializationTests
{
    [Test]
    public async Task InitializeAsync_LoadsSettingsBeforeControlStateForEachBot()
    {
        var calls = new List<string>();
        var store = new TestStateStore(calls);
        var settings = new TestSettingsProvider(calls);
        var bots = CreateBots("Alpha", "Beta");
        var coordinator = CreateCoordinator(settings, store, bots);

        await coordinator.InitializeAsync();

        Assert.That(calls, Is.EqualTo(new[]
        {
            "settings:Alpha", "state:Alpha", "settings:Beta", "state:Beta"
        }));
        bots[0].Received(1).ReloadClient();
    }

    [Test]
    public async Task InitializeAsync_SeedsMissingStatesDisabled()
    {
        var store = new TestStateStore();
        var bots = CreateBots("First", "Second");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(new TestSettingsProvider(), store, bots, initializer);

        await coordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["First"].DesiredEnabled, Is.False);
            Assert.That(store.States["Second"].DesiredEnabled, Is.False);
            Assert.That(
                store.Operations.OrderBy(operation => operation),
                Is.EqualTo(new[] { "insert:First", "insert:Second" }));
            Assert.That(coordinator.CanProcessUpdates("First"), Is.False);
        }

        await initializer.DidNotReceive().EnableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InitializeAsync_PreservesSavedStateAcrossRestarts()
    {
        var store = new TestStateStore();
        store.Seed(new BotControlState
        {
            BotName = "Existing",
            DesiredEnabled = true,
            LastCommandId = "admin-command",
            UpdatedAtUtc = DateTime.UtcNow
        });
        var bots = CreateBots("Existing", "NewBot");
        var coordinator = CreateCoordinator(new TestSettingsProvider(), store, bots);

        await coordinator.InitializeAsync();

        // A new coordinator stands in for a restart, so saved states are read again.
        var restartedCoordinator = CreateCoordinator(new TestSettingsProvider(), store, bots);
        await restartedCoordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["Existing"].DesiredEnabled, Is.True);
            Assert.That(store.States["NewBot"].DesiredEnabled, Is.False);
            Assert.That(store.Operations, Is.EqualTo(new[] { "insert:NewBot" }));
            Assert.That(restartedCoordinator.CanProcessUpdates("Existing"), Is.True);
        }
    }

    [Test]
    public async Task InitializeAsync_WhenAnotherInstanceSeedsFirst_AppliesTheStoredState()
    {
        var store = new TestStateStore
        {
            OnInsert = state => new BotControlState
            {
                BotName = state.BotName,
                DesiredEnabled = true,
                LastCommandId = "other-instance",
                UpdatedAtUtc = DateTime.UtcNow
            }
        };
        var bots = CreateBots("TestBot");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(new TestSettingsProvider(), store, bots, initializer);

        await coordinator.InitializeAsync();

        var status = coordinator.GetStatuses().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(status.DesiredEnabled, Is.True);
            Assert.That(coordinator.CanProcessUpdates("TestBot"), Is.True);
            Assert.That(store.States["TestBot"].LastCommandId, Is.EqualTo("other-instance"));
        }

        await initializer.Received(1).EnableAsync(bots[0], Arg.Any<CancellationToken>());
    }

    [TestCase(BotSettingsState.Incomplete)]
    [TestCase(BotSettingsState.Unreadable)]
    public async Task InitializeAsync_WithUnusableSettings_ReportsNotConfiguredWithTheSavedDesiredState(
        BotSettingsState settingsState)
    {
        var store = new TestStateStore();
        store.Seed(new BotControlState { BotName = "TestBot", DesiredEnabled = true, UpdatedAtUtc = DateTime.UtcNow });
        var settings = new TestSettingsProvider();
        settings.States["TestBot"] = settingsState;
        var bots = CreateBots("TestBot");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(settings, store, bots, initializer);

        await coordinator.InitializeAsync();

        var status = coordinator.GetStatuses().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.Status, Is.EqualTo(BotRuntimeState.NotConfigured));
            Assert.That(status.DesiredEnabled, Is.True);
            Assert.That(status.AppliedEnabled, Is.False);
            Assert.That(coordinator.CanProcessUpdates("TestBot"), Is.False);
            Assert.That(store.Operations, Is.Empty);
        }

        await initializer.DidNotReceive().EnableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
        await initializer.DidNotReceive().DisableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InitializeAsync_NotConfiguredBotWithoutControlState_IsSeededDisabled()
    {
        var store = new TestStateStore();
        var settings = new TestSettingsProvider();
        settings.States["TestBot"] = BotSettingsState.Incomplete;
        var coordinator = CreateCoordinator(settings, store, CreateBots("TestBot"));

        await coordinator.InitializeAsync();

        var status = coordinator.GetStatuses().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["TestBot"].DesiredEnabled, Is.False);
            Assert.That(status.Status, Is.EqualTo(BotRuntimeState.NotConfigured));
            Assert.That(status.DesiredEnabled, Is.False);
            Assert.That(status.AppliedEnabled, Is.False);
        }
    }

    [Test]
    public async Task SetEnabledAsync_ForANotConfiguredBot_ThrowsAndStoresNothing()
    {
        var store = new TestStateStore();
        var settings = new TestSettingsProvider();
        settings.States["TestBot"] = BotSettingsState.Incomplete;
        var bots = CreateBots("TestBot");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(settings, store, bots, initializer);
        await coordinator.InitializeAsync();

        await Assert.ThrowsAsync<BotNotConfiguredException>(() => coordinator.SetEnabledAsync("TestBot", true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["TestBot"].DesiredEnabled, Is.False);
            Assert.That(coordinator.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.NotConfigured));
        }

        await initializer.DidNotReceive().EnableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetWebhookInfoAndDropPendingUpdates_ForANotConfiguredBot_ThrowWithoutCallingTelegram()
    {
        var settings = new TestSettingsProvider();
        settings.States["TestBot"] = BotSettingsState.Incomplete;
        var bots = CreateBots("TestBot");
        var coordinator = CreateCoordinator(settings, new TestStateStore(), bots);
        await coordinator.InitializeAsync();
        bots[0].ClearReceivedCalls();

        await Assert.ThrowsAsync<BotNotConfiguredException>(() => coordinator.GetWebhookInfoAsync("TestBot"));
        await Assert.ThrowsAsync<BotNotConfiguredException>(() => coordinator.DropPendingUpdatesAsync("TestBot"));

        _ = bots[0].DidNotReceive().Client;
        await bots[0].DidNotReceive().DropPendingUpdates(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InitializeAsync_WhenSettingsCannotBeRead_KeepsBotUnknownWithoutReadingControlState()
    {
        var store = new TestStateStore();
        var settings = new TestSettingsProvider();
        settings.States["TestBot"] = BotSettingsState.Unknown;
        var bots = CreateBots("TestBot");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(settings, store, bots, initializer);

        await coordinator.InitializeAsync();

        var status = coordinator.GetStatuses().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.Status, Is.EqualTo(BotRuntimeState.Unknown));
            Assert.That(status.DesiredEnabled, Is.Null);
            Assert.That(coordinator.CanProcessUpdates("TestBot"), Is.False);
            Assert.That(store.ReadCount, Is.Zero);
            Assert.That(store.Operations, Is.Empty);
        }

        await initializer.DidNotReceive().EnableAsync(Arg.Any<IBotService>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RetryAsync_ForANotConfiguredBot_ReloadsSettingsAndAppliesTheSavedState()
    {
        var store = new TestStateStore();
        store.Seed(new BotControlState { BotName = "TestBot", DesiredEnabled = true, UpdatedAtUtc = DateTime.UtcNow });
        var settings = new TestSettingsProvider();
        settings.States["TestBot"] = BotSettingsState.Incomplete;
        var bots = CreateBots("TestBot");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(settings, store, bots, initializer);
        await coordinator.InitializeAsync();
        settings.States["TestBot"] = BotSettingsState.Configured;

        var result = await coordinator.RetryAsync("TestBot");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.LoadCount, Is.EqualTo(2));
            Assert.That(result.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(result.DesiredEnabled, Is.True);
            Assert.That(coordinator.CanProcessUpdates("TestBot"), Is.True);
        }

        await initializer.Received(1).EnableAsync(bots[0], Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RetryAsync_AfterSettingsReadFailure_ReloadsSettings()
    {
        var settings = new TestSettingsProvider();
        settings.States["TestBot"] = BotSettingsState.Unknown;
        var coordinator = CreateCoordinator(settings, new TestStateStore(), CreateBots("TestBot"));
        await coordinator.InitializeAsync();
        settings.States["TestBot"] = BotSettingsState.Configured;

        var result = await coordinator.RetryAsync("TestBot");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.LoadCount, Is.EqualTo(2));
            Assert.That(result.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(result.DesiredEnabled, Is.False);
        }
    }

    [TestCase(BotSettingsState.Incomplete)]
    [TestCase(BotSettingsState.Unknown)]
    public async Task PauseAllForShutdownAsync_SkipsBotsWithoutUsableSettings(BotSettingsState settingsState)
    {
        var settings = new TestSettingsProvider();
        settings.States["Unconfigured"] = settingsState;
        var bots = CreateBots("Configured", "Unconfigured");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(settings, new TestStateStore(), bots, initializer);
        await coordinator.InitializeAsync();
        initializer.ClearReceivedCalls();

        await coordinator.PauseAllForShutdownAsync();

        await initializer.Received(1).DisableAsync(bots[0], Arg.Any<CancellationToken>());
        await initializer.DidNotReceive().DisableAsync(bots[1], Arg.Any<CancellationToken>());
        Assert.That(
            coordinator.GetStatuses().Select(status => status.Status),
            Is.All.EqualTo(BotRuntimeState.Pending));
    }

    [TestCase(BotSettingsState.Incomplete)]
    [TestCase(BotSettingsState.Unknown)]
    public async Task RunWithUpdatesPausedAsync_ForABotWithoutUsableSettings_RunsTheActionWithoutPausing(
        BotSettingsState settingsState)
    {
        var settings = new TestSettingsProvider();
        settings.States["TestBot"] = settingsState;
        var bots = CreateBots("TestBot");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(settings, new TestStateStore(), bots, initializer);
        await coordinator.InitializeAsync();
        var before = coordinator.GetStatuses().Single();

        var result = await coordinator.RunWithUpdatesPausedAsync("TestBot", _ => Task.FromResult("done"));

        Assert.That(result, Is.EqualTo("done"));
        Assert.That(coordinator.GetStatuses().Single(), Is.EqualTo(before));
        await initializer.DidNotReceiveWithAnyArgs().DisableAsync(default!, default);
    }

    [Test]
    public void GetStatuses_ReturnsUnknownStatusForEveryBotOrderedByName()
    {
        var coordinator = CreateCoordinator(
            new TestSettingsProvider(),
            new TestStateStore(),
            CreateBots("Zeta", "Alpha", "Mid"));

        var statuses = coordinator.GetStatuses();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses.Select(status => status.BotName), Is.EqualTo(new[] { "Alpha", "Mid", "Zeta" }));
            Assert.That(statuses.All(status => status.Status == BotRuntimeState.Unknown), Is.True);
            Assert.That(coordinator.CanProcessUpdates("Alpha"), Is.False);
            Assert.That(coordinator.CanProcessUpdates("NotRegistered"), Is.False);
        }
    }

    [Test]
    public async Task InitializeAsync_WhenStateStoreIsUnavailable_KeepsBotUnknownAndGated()
    {
        var store = new TestStateStore { FailReads = true };
        var bots = CreateBots("TestBot");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(new TestSettingsProvider(), store, bots, initializer);

        await coordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coordinator.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Unknown));
            Assert.That(coordinator.CanProcessUpdates("TestBot"), Is.False);
            Assert.That(store.Operations, Is.Empty);
        }

        await initializer.DidNotReceive().EnableAsync(bots[0], Arg.Any<CancellationToken>());
        await initializer.DidNotReceive().DisableAsync(bots[0], Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InitializeAsync_WhenSettingsLoadThrowsUnexpectedly_KeepsThatBotUnknownAndLoadsTheOthers()
    {
        const string failureMessage = "Simulated unexpected settings failure.";
        var store = new TestStateStore();
        store.Seed(new BotControlState { BotName = "Healthy", DesiredEnabled = true, UpdatedAtUtc = DateTime.UtcNow });
        var settings = new TestSettingsProvider();
        settings.Failures["Broken"] = new NotSupportedException(failureMessage);
        var bots = CreateBots("Broken", "Healthy");
        var initializer = CreateInitializer(bots);
        var logger = Substitute.For<ILogger<BotControlCoordinator>>();
        var coordinator = CreateCoordinator(settings, store, bots, initializer, logger);

        await coordinator.InitializeAsync();

        var statuses = coordinator.GetStatuses().ToDictionary(status => status.BotName);
        var loggedText = string.Join(
            "\n",
            logger.ReceivedCalls().SelectMany(call => call.GetArguments()).Select(argument => argument?.ToString()));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses["Broken"].Status, Is.EqualTo(BotRuntimeState.Unknown));
            Assert.That(coordinator.CanProcessUpdates("Broken"), Is.False);
            Assert.That(statuses["Healthy"].Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(coordinator.CanProcessUpdates("Healthy"), Is.True);
            Assert.That(loggedText, Does.Contain(nameof(NotSupportedException)));
            Assert.That(loggedText, Does.Contain(failureMessage));
        }

        await initializer.Received(1).EnableAsync(bots[1], Arg.Any<CancellationToken>());
        settings.Failures.Clear();
        var retried = await coordinator.RetryAsync("Broken");
        Assert.That(retried.Status, Is.EqualTo(BotRuntimeState.Applied));
    }

    [Test]
    public async Task InitializeAsync_WhenApplyThrowsUnexpectedly_KeepsThatBotUnknownAndAppliesTheOthers()
    {
        var store = new TestStateStore();
        store.Seed(new BotControlState { BotName = "Broken", DesiredEnabled = true, UpdatedAtUtc = DateTime.UtcNow });
        store.Seed(new BotControlState { BotName = "Healthy", DesiredEnabled = true, UpdatedAtUtc = DateTime.UtcNow });
        var bots = CreateBots("Broken", "Healthy");
        var initializer = CreateInitializer(bots);
        initializer.EnableAsync(bots[0], Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new NotSupportedException("Simulated unexpected apply failure.")));
        var coordinator = CreateCoordinator(new TestSettingsProvider(), store, bots, initializer);

        await coordinator.InitializeAsync();

        var statuses = coordinator.GetStatuses().ToDictionary(status => status.BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(statuses["Broken"].Status, Is.EqualTo(BotRuntimeState.Unknown));
            Assert.That(statuses["Broken"].DesiredEnabled, Is.True);
            Assert.That(coordinator.CanProcessUpdates("Broken"), Is.False);
            Assert.That(statuses["Healthy"].Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(coordinator.CanProcessUpdates("Healthy"), Is.True);
        }
    }

    [Test]
    public async Task RetryAsync_LoadsDurableStateAfterStartupReadFailure()
    {
        var store = new TestStateStore { FailReads = true };
        var bots = CreateBots("TestBot");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(new TestSettingsProvider(), store, bots, initializer);

        await coordinator.InitializeAsync();
        store.FailReads = false;
        store.Seed(new BotControlState
        {
            BotName = "TestBot",
            DesiredEnabled = true,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var result = await coordinator.RetryAsync("TestBot");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(result.DesiredEnabled, Is.True);
            Assert.That(coordinator.CanProcessUpdates("TestBot"), Is.True);
        }
    }

    [Test]
    public async Task InitializeAsync_CancellationInterruptsPendingProviderOperation()
    {
        var store = new TestStateStore();
        store.Seed(new BotControlState
        {
            BotName = "TestBot",
            DesiredEnabled = true,
            UpdatedAtUtc = DateTime.UtcNow
        });
        var bots = CreateBots("TestBot");
        var initializer = Substitute.For<IBotWebhookInitializer>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        initializer.EnableAsync(bots[0], Arg.Any<CancellationToken>()).Returns(call =>
        {
            started.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
        });
        var coordinator = CreateCoordinator(new TestSettingsProvider(), store, bots, initializer);
        using var cancellation = new CancellationTokenSource();

        var initialization = coordinator.InitializeAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => await initialization);
        Assert.That(coordinator.CanProcessUpdates("TestBot"), Is.False);
        Assert.That(coordinator.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Unknown));
    }

    private static BotControlCoordinator CreateCoordinator(
        IBotSettingsProvider settings,
        TestStateStore store,
        IReadOnlyList<IBotService> bots,
        IBotWebhookInitializer? initializer = null,
        ILogger<BotControlCoordinator>? logger = null) =>
        new(
            settings,
            Substitute.For<IBotSettingsStore>(),
            Substitute.For<ITelegramTokenVerifier>(),
            store,
            BotRegistryFactory.CreateSubstitute([.. bots]),
            initializer ?? CreateInitializer(bots),
            Substitute.For<INotificationService>(),
            logger ?? Substitute.For<ILogger<BotControlCoordinator>>(),
            TestMetrics.BotEvents());

    private static IBotWebhookInitializer CreateInitializer(IReadOnlyList<IBotService> bots)
    {
        var initializer = Substitute.For<IBotWebhookInitializer>();
        foreach (var bot in bots)
        {
            initializer.EnableAsync(bot, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            initializer.DisableAsync(bot, Arg.Any<CancellationToken>()).Returns(true);
        }

        return initializer;
    }

    private static IReadOnlyList<IBotService> CreateBots(params string[] names) =>
        names.Select(name =>
        {
            var bot = Substitute.For<IBotService>();
            bot.Name.Returns(name);
            return bot;
        }).ToArray();

    /// <summary>
    /// Reports a settings state per bot, <see cref="BotSettingsState.Configured"/> unless set, and throws the bot's
    /// entry in <see cref="Failures"/> from <see cref="LoadAsync"/> when it has one.
    /// </summary>
    private sealed class TestSettingsProvider(List<string>? calls = null) : IBotSettingsProvider
    {
        private int _loadCount;

        public Dictionary<string, BotSettingsState> States { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

        public int LoadCount => Volatile.Read(ref _loadCount);

        public Task<BotSettingsState> LoadAsync(string botName, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _loadCount);
            calls?.Add($"settings:{botName}");
            return Failures.TryGetValue(botName, out var failure)
                ? Task.FromException<BotSettingsState>(failure)
                : Task.FromResult(GetState(botName));
        }

        public BotSettingsState GetState(string botName) =>
            States.GetValueOrDefault(botName, BotSettingsState.Configured);

        public BotSettingsView GetView(string botName) => new() { BotName = botName, State = GetState(botName) };

        public Task<BotSettingsState> PersistAsync(
            string botName,
            BotSettingsChange change,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

    }

    private sealed class TestStateStore(List<string>? calls = null) : IBotControlStateStore
    {
        private int _readCount;

        public Dictionary<string, BotControlState> States { get; } = new(StringComparer.Ordinal);
        public List<string> Operations { get; } = [];
        public bool FailReads { get; set; }
        public int ReadCount => Volatile.Read(ref _readCount);

        /// <summary>
        /// When set, simulates another instance storing the returned state just before this insert.
        /// </summary>
        public Func<BotControlState, BotControlState>? OnInsert { get; set; }

        public void Seed(BotControlState state) => States[state.BotName] = state;

        public Task<BotControlState?> GetAsync(string botName, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            calls?.Add($"state:{botName}");
            if (FailReads)
            {
                throw new MongoException("Simulated state-store read failure.");
            }

            States.TryGetValue(botName, out var state);
            return Task.FromResult(state);
        }

        public Task<bool> TryInsertAsync(BotControlState state, CancellationToken cancellationToken = default)
        {
            Operations.Add($"insert:{state.BotName}");
            if (OnInsert is not null)
            {
                States[state.BotName] = OnInsert(state);
            }

            return Task.FromResult(States.TryAdd(state.BotName, state));
        }

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

            var updated = state with
            {
                DesiredEnabled = desiredEnabled,
                LastCommandId = commandId,
                UpdatedAtUtc = updatedAtUtc
            };
            States[botName] = updated;
            return Task.FromResult<BotControlState?>(updated);
        }
    }
}
