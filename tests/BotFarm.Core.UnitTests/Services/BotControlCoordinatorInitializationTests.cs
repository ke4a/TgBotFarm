using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotControlCoordinatorInitializationTests
{
    [Test]
    public async Task InitializeAsync_SeedsMissingStatesFromBotConfigEnabledAndDefaultsDisabled()
    {
        var store = new TestStateStore();
        var bots = CreateBots("Enabled", "Disabled", "Missing");
        var coordinator = CreateCoordinator(
            CreateBotConfigs(new Dictionary<string, string?>
            {
                ["Bots:Enabled:BotConfig:Enabled"] = "true",
                ["Bots:Disabled:BotConfig:Enabled"] = "false"
            }, "Enabled", "Disabled", "Missing"),
            store,
            bots);

        await coordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["Enabled"].DesiredEnabled, Is.True);
            Assert.That(store.States["Disabled"].DesiredEnabled, Is.False);
            Assert.That(store.States["Missing"].DesiredEnabled, Is.False);
            Assert.That(
                store.Operations.OrderBy(operation => operation),
                Is.EqualTo(new[] { "insert:Disabled", "insert:Enabled", "insert:Missing" }));
        }
    }

    [Test]
    public async Task InitializeAsync_PreservesSavedStateAndSeedsMissingBotsFromCurrentConfiguration()
    {
        var store = new TestStateStore();
        store.Seed(new BotControlState
        {
            BotName = "Existing",
            DesiredEnabled = false,
            LastCommandId = "admin-command",
            UpdatedAtUtc = DateTime.UtcNow
        });
        var bots = CreateBots("Existing", "NewBot", "Unconfigured");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bots:Existing:BotConfig:Enabled"] = "true",
                ["Bots:NewBot:BotConfig:Enabled"] = "true"
            })
            .Build();
        var coordinator = CreateCoordinator(
            CreateBotConfigs(configuration, "Existing", "NewBot", "Unconfigured"), store, bots);

        await coordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["Existing"].DesiredEnabled, Is.False);
            Assert.That(store.States["NewBot"].DesiredEnabled, Is.True);
            Assert.That(store.States["Unconfigured"].DesiredEnabled, Is.False);
        }

        // A new coordinator stands in for a restart, so saved states are read again.
        configuration["Bots:NewBot:BotConfig:Enabled"] = "false";
        var restartedCoordinator = CreateCoordinator(
            CreateBotConfigs(configuration, "Existing", "NewBot", "Unconfigured"), store, bots);
        await restartedCoordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["Existing"].DesiredEnabled, Is.False);
            Assert.That(store.States["NewBot"].DesiredEnabled, Is.True);
            Assert.That(store.Operations.Count(operation => operation == "insert:NewBot"), Is.EqualTo(1));
            Assert.That(store.Operations.Count(operation => operation == "insert:Unconfigured"), Is.EqualTo(1));
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
        var coordinator = CreateCoordinator(CreateBotConfigs("TestBot"), store, bots, initializer);

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

    [Test]
    public void GetStatuses_ReturnsUnknownStatusForEveryBotOrderedByName()
    {
        var coordinator = CreateCoordinator(CreateBotConfigs("Zeta", "Alpha", "Mid"), new TestStateStore(), CreateBots("Zeta", "Alpha", "Mid"));

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
        var bots = CreateBots("ConfiguredEnabled");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(
            CreateBotConfigs(
                new Dictionary<string, string?> { ["Bots:ConfiguredEnabled:BotConfig:Enabled"] = "true" },
                "ConfiguredEnabled"),
            store,
            bots,
            initializer);

        await coordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coordinator.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Unknown));
            Assert.That(coordinator.CanProcessUpdates("ConfiguredEnabled"), Is.False);
            Assert.That(store.Operations, Is.Empty);
        }

        await initializer.DidNotReceive().EnableAsync(bots[0], Arg.Any<CancellationToken>());
        await initializer.DidNotReceive().DisableAsync(bots[0], Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RetryAsync_LoadsDurableStateAfterStartupReadFailure()
    {
        var store = new TestStateStore { FailReads = true };
        var bots = CreateBots("TestBot");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(CreateBotConfigs("TestBot"), store, bots, initializer);

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
        var coordinator = CreateCoordinator(CreateBotConfigs("TestBot"), store, bots, initializer);
        using var cancellation = new CancellationTokenSource();

        var initialization = coordinator.InitializeAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => await initialization);
        Assert.That(coordinator.CanProcessUpdates("TestBot"), Is.False);
        Assert.That(coordinator.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Unknown));
    }

    private static BotControlCoordinator CreateCoordinator(
        IOptionsMonitor<BotConfig> botConfigs,
        TestStateStore store,
        IReadOnlyList<IBotService> bots,
        IBotWebhookInitializer? initializer = null) =>
        new(
            botConfigs,
            store,
            BotRegistryFactory.CreateSubstitute([.. bots]),
            initializer ?? CreateInitializer(bots),
            Substitute.For<INotificationService>(),
            Substitute.For<ILogger<BotControlCoordinator>>());

    private static IOptionsMonitor<BotConfig> CreateBotConfigs(params string[] botNames) =>
        CreateBotConfigs(new ConfigurationBuilder().Build(), botNames);

    private static IOptionsMonitor<BotConfig> CreateBotConfigs(
        IDictionary<string, string?> settings,
        params string[] botNames) =>
        CreateBotConfigs(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), botNames);

    private static IOptionsMonitor<BotConfig> CreateBotConfigs(IConfiguration configuration, params string[] botNames)
    {
        var services = new ServiceCollection();
        foreach (var botName in botNames)
        {
            services.Configure<BotConfig>(botName, configuration.GetSection($"Bots:{botName}:BotConfig"));
        }

        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<BotConfig>>();
    }

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

    private sealed class TestStateStore : IBotControlStateStore
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
