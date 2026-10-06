using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotControlCoordinatorTests
{
    private const string BotName = "TestBot";

    private InMemoryBotControlStateStore _stateStore = default!;
    private IBotService _botService = default!;
    private IBotWebhookInitializer _webhookInitializer = default!;
    private BotControlCoordinator _sut = default!;

    [SetUp]
    public async Task SetUp()
    {
        _stateStore = new InMemoryBotControlStateStore();
        _stateStore.Seed(new BotControlState
        {
            BotName = BotName,
            DesiredEnabled = false,
            UpdatedAtUtc = DateTime.UtcNow
        });

        _botService = Substitute.For<IBotService>();
        _botService.Name.Returns(BotName);
        _webhookInitializer = Substitute.For<IBotWebhookInitializer>();
        _webhookInitializer.DisableAsync(_botService, Arg.Any<CancellationToken>()).Returns(true);
        _sut = new BotControlCoordinator(
            new ConfigurationBuilder().Build(),
            _stateStore,
            [_botService],
            _webhookInitializer,
            Substitute.For<ILogger<BotControlCoordinator>>());
        await _sut.InitializeAsync();
        _webhookInitializer.ClearReceivedCalls();
    }

    [Test]
    public async Task SetEnabledAsync_PersistsAndAppliesExplicitTargetBeforeReturning()
    {
        var status = await _sut.SetEnabledAsync(BotName, true);

        var saved = await _stateStore.GetAsync(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved!.DesiredEnabled, Is.True);
            Assert.That(saved.LastCommandId, Is.Not.Null.And.Not.Empty);
            Assert.That(status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(status.DesiredEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
        }

        await _webhookInitializer.Received(1).EnableAsync(_botService, Arg.Any<CancellationToken>());
    }

    [Test]
    public void SetEnabledAsync_WhenPersistenceFails_DoesNotAcceptOrApplyCommand()
    {
        _stateStore.FailWrites = true;

        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SetEnabledAsync(BotName, true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Unknown));
            Assert.That(_sut.GetStatuses().Single().DesiredEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }

        _webhookInitializer.DidNotReceive().EnableAsync(_botService, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RetryAsync_RetriesUnconfirmedDisableAfterStateStoreRecovers()
    {
        await _sut.SetEnabledAsync(BotName, true);
        _stateStore.FailWrites = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SetEnabledAsync(BotName, false));

        var failedStatus = _sut.GetStatuses().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failedStatus.Status, Is.EqualTo(BotRuntimeState.Unknown));
            Assert.That(failedStatus.DesiredEnabled, Is.False);
            Assert.That(failedStatus.AppliedEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }

        _stateStore.FailWrites = false;
        var retryStatus = await _sut.RetryAsync(BotName);
        var savedState = await _stateStore.GetAsync(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(retryStatus.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(retryStatus.DesiredEnabled, Is.False);
            Assert.That(retryStatus.AppliedEnabled, Is.False);
            Assert.That(savedState!.DesiredEnabled, Is.False);
            Assert.That(_stateStore.AttemptedCommandIds[^1], Is.EqualTo(_stateStore.AttemptedCommandIds[^2]));
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }

        await _webhookInitializer.Received(1).DisableAsync(_botService, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Disable_ClosesInboundGateBeforeExternalPauseIsApplied()
    {
        await _sut.SetEnabledAsync(BotName, true);
        Assert.That(_sut.CanProcessUpdates(BotName), Is.True);

        var pauseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePause = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _webhookInitializer.DisableAsync(_botService, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                pauseStarted.TrySetResult();
                return releasePause.Task;
            });

        var disabling = _sut.SetEnabledAsync(BotName, false);
        await pauseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        await Assert.ThrowsAsync<BotControlBusyException>(() => _sut.SetEnabledAsync(BotName, true));
        releasePause.SetResult(true);
        await disabling;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(_sut.GetStatuses().Single().AppliedEnabled, Is.False);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }

        await _webhookInitializer.Received(1).DisableAsync(_botService, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InitializeAsync_DoesNotRepeatAnAlreadyAppliedState()
    {
        await _sut.InitializeAsync();

        await _webhookInitializer.DidNotReceive().DisableAsync(_botService, Arg.Any<CancellationToken>());
        Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
    }

    [Test]
    public async Task SetEnabledAsync_RejectsCommandsWhileTelegramTransitionIsInProgress()
    {
        var transitionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTransition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _webhookInitializer.EnableAsync(_botService, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                transitionStarted.TrySetResult();
                return releaseTransition.Task;
            });

        var command = _sut.SetEnabledAsync(BotName, true);
        await transitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<BotControlBusyException>(() => _sut.SetEnabledAsync(BotName, false));
        Assert.That(_sut.CanProcessUpdates(BotName), Is.False);

        releaseTransition.SetResult();
        await command.WaitAsync(TimeSpan.FromSeconds(5));

        var status = _sut.GetStatuses().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.DesiredEnabled, Is.True);
            Assert.That(status.AppliedEnabled, Is.True);
            Assert.That(status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
        }

        await _webhookInitializer.DidNotReceive().DisableAsync(_botService, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConcurrentCommands_RejectNewCommandWhileStateWriteIsInProgress()
    {
        await _sut.SetEnabledAsync(BotName, true);
        Assert.That(_sut.CanProcessUpdates(BotName), Is.True);

        var persistedCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stateStore.StateWritePersisted = persistedCommand;
        _stateStore.ReleaseStateWrite = releaseCommand;

        var command = _sut.SetEnabledAsync(BotName, false);
        await persistedCommand.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<BotControlBusyException>(() => _sut.SetEnabledAsync(BotName, true));
        Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        releaseCommand.SetResult();
        var result = await command;
        var persistedState = await _stateStore.GetAsync(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DesiredEnabled, Is.False);
            Assert.That(_sut.GetStatuses().Single().DesiredEnabled, Is.False);
            Assert.That(persistedState!.DesiredEnabled, Is.False);
        }
    }

    [Test]
    public async Task SetEnabledAsync_WhenCanceledDuringPersistence_MarksUnknownAndCanRetryDurableTarget()
    {
        var persistedCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommand = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stateStore.StateWritePersisted = persistedCommand;
        _stateStore.ReleaseStateWrite = releaseCommand;
        using var cancellationTokenSource = new CancellationTokenSource();

        var command = _sut.SetEnabledAsync(BotName, true, cancellationTokenSource.Token);
        await persistedCommand.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationTokenSource.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(() => command);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Unknown));
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }

        _stateStore.StateWritePersisted = null;
        _stateStore.ReleaseStateWrite = null;
        await Assert.ThrowsAsync<BotControlBusyException>(() => _sut.SetEnabledAsync(BotName, false));
        await _sut.RetryAsync(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sut.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(_sut.GetStatuses().Single().AppliedEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
        }
    }

    [Test]
    public async Task SetEnabledAsync_ReadsBackAmbiguousWriteByCommandId()
    {
        _stateStore.AmbiguousWrites = true;

        var status = await _sut.SetEnabledAsync(BotName, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(status.DesiredEnabled, Is.True);
            Assert.That((await _stateStore.GetAsync(BotName))!.LastCommandId, Is.Not.Null.And.Not.Empty);
        }
    }

    [Test]
    public async Task SetEnabledAsync_RetriesTransientFailureInline()
    {
        var attempts = 0;
        _webhookInitializer.EnableAsync(_botService, Arg.Any<CancellationToken>())
            .Returns<Task>(_ =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new HttpRequestException("temporary failure"))
                    : Task.CompletedTask;
            });

        var status = await _sut.SetEnabledAsync(BotName, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
        }
    }

    [Test]
    public async Task SetEnabledAsync_ExhaustedRetriesAreSanitizedAndCanBeRetriedExplicitly()
    {
        _webhookInitializer.EnableAsync(_botService, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("token=secret"));
        var failedStatus = await _sut.SetEnabledAsync(BotName, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failedStatus.Status, Is.EqualTo(BotRuntimeState.Error));
            Assert.That(failedStatus.LastError, Does.Not.Contain("secret"));
            Assert.That(failedStatus.LastError, Does.Contain("3 attempts"));
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }
        await _webhookInitializer.Received(3).EnableAsync(_botService, Arg.Any<CancellationToken>());

        await Assert.ThrowsAsync<BotControlBusyException>(() => _sut.SetEnabledAsync(BotName, false));

        _webhookInitializer.EnableAsync(_botService, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var recoveredStatus = await _sut.RetryAsync(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(recoveredStatus.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(_sut.CanProcessUpdates(BotName), Is.True);
        }
    }

    [Test]
    public async Task SetEnabledAsync_FailedPauseRemainsErrorUntilExplicitRetrySucceeds()
    {
        await _sut.SetEnabledAsync(BotName, true);
        _webhookInitializer.DisableAsync(_botService, Arg.Any<CancellationToken>()).Returns(false);

        var failedStatus = await _sut.SetEnabledAsync(BotName, false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failedStatus.Status, Is.EqualTo(BotRuntimeState.Error));
            Assert.That(failedStatus.AppliedEnabled, Is.True);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }

        _webhookInitializer.DisableAsync(_botService, Arg.Any<CancellationToken>()).Returns(true);
        var recoveredStatus = await _sut.RetryAsync(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(recoveredStatus.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(recoveredStatus.AppliedEnabled, Is.False);
            Assert.That(_sut.CanProcessUpdates(BotName), Is.False);
        }
    }

    private sealed class InMemoryBotControlStateStore : IBotControlStateStore
    {
        private readonly Dictionary<string, BotControlState> _states = new(StringComparer.Ordinal);

        public List<string> AttemptedCommandIds { get; } = [];
        public bool FailWrites { get; set; }
        public bool AmbiguousWrites { get; set; }
        public TaskCompletionSource? StateWritePersisted { get; set; }
        public TaskCompletionSource? ReleaseStateWrite { get; set; }

        public void Seed(BotControlState state) => _states[state.BotName] = state;

        public Task<BotControlState?> GetAsync(string botName, CancellationToken cancellationToken = default)
        {
            _states.TryGetValue(botName, out var state);
            return Task.FromResult(state);
        }

        public Task<bool> TryInsertAsync(BotControlState state, CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new InvalidOperationException("State store unavailable.");
            }

            return Task.FromResult(_states.TryAdd(state.BotName, state));
        }

        public async Task<BotControlState?> TrySetDesiredStateAsync(
            string botName,
            bool desiredEnabled,
            string commandId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken = default)
        {
            AttemptedCommandIds.Add(commandId);
            if (FailWrites)
            {
                throw new InvalidOperationException("State store unavailable.");
            }

            if (!_states.TryGetValue(botName, out var state))
            {
                return null;
            }

            var updated = state with
            {
                DesiredEnabled = desiredEnabled,
                LastCommandId = commandId,
                UpdatedAtUtc = updatedAtUtc
            };
            _states[botName] = updated;
            if (AmbiguousWrites)
            {
                AmbiguousWrites = false;
                throw new MongoDB.Driver.MongoException("Write acknowledgment was lost.");
            }

            if (StateWritePersisted is not null && ReleaseStateWrite is not null)
            {
                StateWritePersisted.TrySetResult();
                await ReleaseStateWrite.Task.WaitAsync(cancellationToken);
            }

            return updated;
        }

    }
}