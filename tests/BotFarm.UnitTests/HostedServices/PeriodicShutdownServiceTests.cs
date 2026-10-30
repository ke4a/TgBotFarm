using BotFarm.HostedServices;
using BotFarm.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using static BotFarm.UnitTests.HostedServices.HostedServiceTestHelpers;

namespace BotFarm.UnitTests.HostedServices;

[TestFixture]
public class PeriodicShutdownServiceTests
{
    private IHostApplicationLifetime _appLifetime = null!;
    private ILogger<PeriodicShutdownService> _logger = null!;
    private ManualTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _appLifetime = Substitute.For<IHostApplicationLifetime>();
        _logger = Substitute.For<ILogger<PeriodicShutdownService>>();
        _timeProvider = new ManualTimeProvider();
    }

    private PeriodicShutdownService CreateService(string? shutdownEveryHours)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PeriodicShutdownService.ShutdownEveryHoursKey] = shutdownEveryHours
            })
            .Build();

        return new PeriodicShutdownService(_appLifetime, configuration, _timeProvider, _logger);
    }

    [TestCase(null)]
    [TestCase("0")]
    [TestCase("-3")]
    public async Task ExecuteAsync_DisabledInterval_ReturnsWithoutTimerOrShutdown(string? value)
    {
        using var service = CreateService(value);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(_timeProvider.ActiveTimerCount, Is.Zero);
        _appLifetime.DidNotReceive().StopApplication();
    }

    [Test]
    public async Task ExecuteAsync_PositiveInterval_StopsApplicationAfterInterval()
    {
        using var service = CreateService("6");
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _timeProvider.ActiveTimerCount == 1, "the shutdown timer is created");

        _timeProvider.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        _appLifetime.DidNotReceive().StopApplication();

        _timeProvider.Advance(TimeSpan.FromMinutes(1));
        await WaitUntilAsync(() => _appLifetime.ReceivedCalls().Any(), "the application is stopped");
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        _appLifetime.Received(1).StopApplication();
        var entries = _logger.LoggedEntries();
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entries[0].Message, Is.EqualTo("Scheduled shutdown."));
    }

    [Test]
    public async Task ExecuteAsync_StoppedBeforeInterval_EndsWithoutShutdown()
    {
        using var service = CreateService("6");
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _timeProvider.ActiveTimerCount == 1, "the shutdown timer is created");

        await service.StopAsync(CancellationToken.None);

        Assert.That(service.ExecuteTask!.IsCompletedSuccessfully, Is.True);
        _appLifetime.DidNotReceive().StopApplication();
    }
}
