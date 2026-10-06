using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotControlCoordinatorInitializationTests
{
    [Test]
    public async Task InitializeAsync_SeedsMissingStatesFromConfigurationAndDefaultsDisabled()
    {
        var store = new TestStateStore();
        var bots = CreateBots("Enabled", "Disabled", "Missing", "Invalid");
        var coordinator = CreateCoordinator(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Bots:Enabled:BotConfig:Enabled"] = "true",
                    ["Bots:Disabled:BotConfig:Enabled"] = "false",
                    ["Bots:Invalid:BotConfig:Enabled"] = "not-a-boolean"
                })
                .Build(),
            store,
            bots);

        await coordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["Enabled"].DesiredEnabled, Is.True);
            Assert.That(store.States["Disabled"].DesiredEnabled, Is.False);
            Assert.That(store.States["Missing"].DesiredEnabled, Is.False);
            Assert.That(store.States["Invalid"].DesiredEnabled, Is.False);
            Assert.That(
                store.Operations.OrderBy(operation => operation),
                Is.EqualTo(new[] { "insert:Disabled", "insert:Enabled", "insert:Invalid", "insert:Missing" }));
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
        var coordinator = CreateCoordinator(configuration, store, bots);

        await coordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["Existing"].DesiredEnabled, Is.False);
            Assert.That(store.States["NewBot"].DesiredEnabled, Is.True);
            Assert.That(store.States["Unconfigured"].DesiredEnabled, Is.False);
        }

        configuration["Bots:NewBot:BotConfig:Enabled"] = "false";
        await coordinator.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.States["Existing"].DesiredEnabled, Is.False);
            Assert.That(store.States["NewBot"].DesiredEnabled, Is.True);
            Assert.That(store.Operations.Count(operation => operation == "insert:NewBot"), Is.EqualTo(1));
            Assert.That(store.Operations.Count(operation => operation == "insert:Unconfigured"), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task InitializeAsync_WhenStateStoreIsUnavailable_KeepsBotUnknownAndGated()
    {
        var store = new TestStateStore { FailReads = true };
        var bots = CreateBots("ConfiguredEnabled");
        var initializer = CreateInitializer(bots);
        var coordinator = CreateCoordinator(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Bots:ConfiguredEnabled:BotConfig:Enabled"] = "true"
                })
                .Build(),
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
        var coordinator = CreateCoordinator(new ConfigurationBuilder().Build(), store, bots, initializer);

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
        var coordinator = CreateCoordinator(new ConfigurationBuilder().Build(), store, bots, initializer);
        using var cancellation = new CancellationTokenSource();

        var initialization = coordinator.InitializeAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () => await initialization);
        Assert.That(coordinator.CanProcessUpdates("TestBot"), Is.False);
        Assert.That(coordinator.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Unknown));
    }

    private static BotControlCoordinator CreateCoordinator(
        IConfiguration configuration,
        TestStateStore store,
        IReadOnlyList<IBotService> bots,
        IBotWebhookInitializer? initializer = null) =>
        new(
            configuration,
            store,
            bots,
            initializer ?? CreateInitializer(bots),
            Substitute.For<ILogger<BotControlCoordinator>>());

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
