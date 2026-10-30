using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.HostedServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.UnitTests.HostedServices;

[TestFixture]
public class BotControlStartupServiceTests
{
    private CancellationTokenSource _applicationStarted = null!;
    private IHostApplicationLifetime _appLifetime = null!;
    private IBotControlStateStore _stateStore = null!;
    private TaskCompletionSource<CancellationToken> _storeToken = null!;
    private BotControlCoordinator _coordinator = null!;

    [SetUp]
    public void SetUp()
    {
        _applicationStarted = new CancellationTokenSource();
        _appLifetime = Substitute.For<IHostApplicationLifetime>();
        _appLifetime.ApplicationStarted.Returns(_applicationStarted.Token);

        // Initialization reads each bot's saved state first; block there until the token is cancelled.
        _storeToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        _stateStore = Substitute.For<IBotControlStateStore>();
        _stateStore.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var token = call.ArgAt<CancellationToken>(1);
            _storeToken.TrySetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            return (BotControlState?)null;
        });

        var bot = Substitute.For<IBotService>();
        bot.Name.Returns("BotA");
        _coordinator = new BotControlCoordinator(
            new ConfigurationBuilder().Build(),
            _stateStore,
            [bot],
            Substitute.For<IBotWebhookInitializer>(),
            Substitute.For<INotificationService>(),
            Substitute.For<ILogger<BotControlCoordinator>>());
    }

    [TearDown]
    public void TearDown()
    {
        _applicationStarted.Dispose();
    }

    [Test]
    public async Task ExecuteAsync_AfterApplicationStarted_InitializesWithStoppingTokenAndStopsQuietly()
    {
        using var service = new BotControlStartupService(_coordinator, _appLifetime);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await _stateStore.DidNotReceiveWithAnyArgs().GetAsync(default!, default);

        await _applicationStarted.CancelAsync();
        var token = await _storeToken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(token.IsCancellationRequested, Is.False);

        await service.StopAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(service.ExecuteTask!.IsCompletedSuccessfully, Is.True);
            Assert.That(_coordinator.GetStatuses().Single().Status, Is.EqualTo(BotRuntimeState.Unknown));
        }
    }

    [Test]
    public async Task ExecuteAsync_StoppedBeforeApplicationStarted_EndsWithoutInitializing()
    {
        using var service = new BotControlStartupService(_coordinator, _appLifetime);
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        // The host may cancel the task before it starts running, which also counts as a clean stop.
        Assert.That(service.ExecuteTask!.IsCompleted && !service.ExecuteTask.IsFaulted, Is.True);
        await _stateStore.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
    }
}
