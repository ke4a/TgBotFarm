using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Pages;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.UnitTests.Pages;

[TestFixture]
public class DashboardBotControlTests
{
    private TestableDashboard _dashboard = default!;
    private IBotControlService _controlService = default!;

    [SetUp]
    public void SetUp()
    {
        _controlService = Substitute.For<IBotControlService>();
        _dashboard = new TestableDashboard();
        _dashboard.SetDependencies(_controlService, Substitute.For<ILogger<Dashboard>>());
    }

    [Test]
    public void LoadBotStatuses_PreservesUnknownAppliedAndErrorStates()
    {
        _controlService.GetStatuses().Returns(
        [
            new BotRuntimeStatus("UnknownBot", null, null, BotRuntimeState.Unknown, null, null),
            new BotRuntimeStatus("AppliedBot", false, false, BotRuntimeState.Applied, DateTime.UtcNow, null),
            new BotRuntimeStatus("ErrorBot", true, false, BotRuntimeState.Error, DateTime.UtcNow,
                "Transition failed after 3 attempts.")
        ]);

        _dashboard.RefreshBotStatuses();

        Assert.That(
            _dashboard.Statuses.Select(status => status.Status),
            Is.EqualTo(new[] { BotRuntimeState.Unknown, BotRuntimeState.Applied, BotRuntimeState.Error }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SetBotEnabled_SubmitsExplicitTarget(bool enabled)
    {
        await _dashboard.ClickToggle("TestBot", enabled);

        await _controlService.Received(1).SetEnabledAsync("TestBot", enabled, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetBotEnabled_ShowsApplyingUntilTheRequestReturnsFinalStatus()
    {
        var response = new TaskCompletionSource<BotRuntimeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        _controlService.SetEnabledAsync("TestBot", true, Arg.Any<CancellationToken>())
            .Returns(response.Task);
        IReadOnlyList<BotRuntimeStatus> pendingStatuses =
        [
            new BotRuntimeStatus("TestBot", true, false, BotRuntimeState.Pending, DateTime.UtcNow, null)
        ];
        IReadOnlyList<BotRuntimeStatus> appliedStatuses =
        [
            new BotRuntimeStatus("TestBot", true, true, BotRuntimeState.Applied, DateTime.UtcNow, null)
        ];
        var statusReadCount = 0;
        _controlService.GetStatuses().Returns(_ =>
        {
            return statusReadCount++ == 0 ? pendingStatuses : appliedStatuses;
        });

        var request = _dashboard.ClickToggle("TestBot", true);
        Assert.That(_dashboard.Statuses.Single().Status, Is.EqualTo(BotRuntimeState.Pending));
        Assert.That(_dashboard.Statuses.Single().DesiredEnabled, Is.True);
        Assert.That(_dashboard.IsApplying(_dashboard.Statuses.Single()), Is.True);

        response.SetResult(appliedStatuses[0]);
        await request;

        Assert.That(_dashboard.Statuses.Single().Status, Is.EqualTo(BotRuntimeState.Applied));
        Assert.That(statusReadCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SetBotEnabled_WhenDatabaseReturns_ShowsRetryableErrorWithoutPolling()
    {
        _controlService.SetEnabledAsync("TestBot", true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BotRuntimeStatus>(
                new InvalidOperationException("secret-bearing provider detail")));
        IReadOnlyList<BotRuntimeStatus> unknownStatuses =
        [
            new BotRuntimeStatus("TestBot", true, false, BotRuntimeState.Unknown, DateTime.UtcNow, null)
        ];
        var statusReadCount = 0;
        _controlService.GetStatuses().Returns(_ =>
        {
            statusReadCount++;
            return unknownStatuses;
        });

        await _dashboard.ClickToggle("TestBot", true);

        var unknownStatus = _dashboard.Statuses.Single();
        Assert.That(_dashboard.ProblemMessage(unknownStatus), Does.Contain("save and apply"));
        Assert.That(_dashboard.ProblemMessage(unknownStatus), Does.Not.Contain("secret-bearing"));
        Assert.That(_dashboard.SwitchDisabled(unknownStatus), Is.True);
        Assert.That(statusReadCount, Is.EqualTo(2));
        await _controlService.DidNotReceive().RetryAsync("TestBot", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RetryBotState_SubmitsExplicitRetry()
    {
        var applied = new BotRuntimeStatus("TestBot", true, true, BotRuntimeState.Applied, DateTime.UtcNow, null);
        _controlService.RetryAsync("TestBot", Arg.Any<CancellationToken>()).Returns(applied);
        _controlService.GetStatuses().Returns([applied]);

        await _dashboard.ClickRetry("TestBot");

        await _controlService.Received(1).RetryAsync("TestBot", Arg.Any<CancellationToken>());
        Assert.That(_dashboard.Statuses.Single().Status, Is.EqualTo(BotRuntimeState.Applied));
    }

    [Test]
    public void ProblemMessage_IsShownOnlyForUnknownOrFailedStates()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _dashboard.ProblemMessage(new BotRuntimeStatus(
                    "AppliedBot", true, true, BotRuntimeState.Applied, DateTime.UtcNow, null)),
                Is.Null);
            Assert.That(
                _dashboard.ProblemMessage(new BotRuntimeStatus(
                    "PendingBot", true, false, BotRuntimeState.Pending, DateTime.UtcNow, null)),
                Is.Null);
            Assert.That(
                _dashboard.ProblemMessage(new BotRuntimeStatus(
                    "UnknownBot", null, null, BotRuntimeState.Unknown, null, null)),
                Does.Contain("unknown"));
            Assert.That(
                _dashboard.ProblemMessage(new BotRuntimeStatus(
                    "ErrorBot", true, false, BotRuntimeState.Error, DateTime.UtcNow, null)),
                Does.Contain("saved state"));
        }
    }

    [Test]
    public void SwitchIsDisabledUntilTheSavedStateIsKnownAndNotApplying()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _dashboard.SwitchDisabled(new BotRuntimeStatus(
                    "UnknownBot", null, null, BotRuntimeState.Unknown, null, null)),
                Is.True);
            Assert.That(
                _dashboard.SwitchDisabled(new BotRuntimeStatus(
                    "PendingBot", true, false, BotRuntimeState.Pending, DateTime.UtcNow, null)),
                Is.True);
            Assert.That(
                _dashboard.SwitchDisabled(new BotRuntimeStatus(
                    "AppliedBot", false, false, BotRuntimeState.Applied, DateTime.UtcNow, null)),
                Is.False);
            Assert.That(
                _dashboard.SwitchDisabled(new BotRuntimeStatus(
                    "ErrorBot", true, false, BotRuntimeState.Error, DateTime.UtcNow, null)),
                Is.True);
        }
    }

    private sealed class TestableDashboard : Dashboard
    {
        public IReadOnlyList<BotRuntimeStatus> Statuses => GetField<List<BotRuntimeStatus>>("_botStatuses");

        public void SetDependencies(IBotControlService controlService, ILogger<Dashboard> logger)
        {
            BotControlService = controlService;
            Logger = logger;
        }

        public void RefreshBotStatuses() => LoadBotStatuses();
        public Task ClickToggle(string botName, bool enabled) => base.SetBotEnabled(botName, enabled);
        public Task ClickRetry(string botName) => base.RetryBotState(botName);
        public string? ProblemMessage(BotRuntimeStatus status) => GetBotProblemMessage(status);
        public bool SwitchDisabled(BotRuntimeStatus status) => IsBotSwitchDisabled(status);
        public bool IsApplying(BotRuntimeStatus status) => IsBotApplying(status);
        protected override Task RefreshUiAsync() => Task.CompletedTask;

        private T GetField<T>(string name)
        {
            var field = typeof(Dashboard).GetField(
                name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (T)field!.GetValue(this)!;
        }
    }
}
