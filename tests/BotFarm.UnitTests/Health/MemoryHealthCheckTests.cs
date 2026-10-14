using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
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
        var healthCheck = CreateHealthCheck(long.MaxValue, notificationService);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(result.Data, Does.ContainKey("AllocatedBytes"));
            Assert.That(result.Data, Does.ContainKey("GCTotal"));
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
        long threshold,
        INotificationService notificationService)
    {
        var options = Substitute.For<IOptionsMonitor<MemoryCheckOptions>>();
        options.Get(Arg.Any<string>()).Returns(new MemoryCheckOptions { Threshold = threshold });
        return new MemoryHealthCheck(
            options,
            [new BotIdentity("TestBot")],
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
