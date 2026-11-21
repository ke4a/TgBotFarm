using BotFarm.Core.Abstractions;
using BotFarm.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.UnitTests.Health;

[TestFixture]
public class MemoryHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_BelowThreshold_ReturnsHealthyWithoutNotification()
    {
        var notificationService = Substitute.For<INotificationService>();
        var healthCheck = CreateHealthCheck(1024L * 1024L, notificationService);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(result.Data["WorkingSetBytes"], Is.EqualTo(Environment.WorkingSet).Within(64L * 1024 * 1024));
            Assert.That(result.Data, Does.ContainKey("GCHeapSizeBytes"));
            Assert.That(result.Data["ThresholdBytes"], Is.EqualTo(1024L * 1024L * 1024L * 1024L));
        }

        await notificationService.DidNotReceive()
            .SendWarningNotification(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task CheckHealthAsync_AtOrAboveThreshold_ReturnsUnhealthyAndNotifiesBot()
    {
        var notificationService = Substitute.For<INotificationService>();
        var healthCheck = CreateHealthCheck(0, notificationService);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck));

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
        await notificationService.Received(1).SendWarningNotification(
            Arg.Is<string>(message => message.Contains("exceeded threshold", StringComparison.Ordinal)),
            "TestBot");
    }

    private static MemoryHealthCheck CreateHealthCheck(
        long thresholdMb,
        INotificationService notificationService)
    {
        var options = Substitute.For<IOptionsMonitor<MemoryCheckOptions>>();
        options.Get(Arg.Any<string>()).Returns(new MemoryCheckOptions { ThresholdMb = thresholdMb });
        return new MemoryHealthCheck(
            options,
            [TestBotModules.Create("TestBot")],
            Substitute.For<ILogger<MemoryHealthCheck>>(),
            notificationService);
    }

    private static HealthCheckContext CreateContext(IHealthCheck healthCheck)
    {
        return new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("memory", _ => healthCheck, null, [])
        };
    }
}
