using BotFarm.Core.Abstractions;
using BotFarm.Core.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using TestBot.Abstractions;

namespace TestBot.UnitTests.Health;

[TestFixture]
public class TestBotStatsHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenChatCountLoads_ReturnsHealthyResultWithChatCount()
    {
        var databaseService = Substitute.For<ITestBotDatabaseService>();
        databaseService.GetChatCount().Returns(Task.FromResult(3L));
        var healthCheck = new BotStatsHealthCheck(databaseService, "TestBot");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(result.Data["ChatsCount"], Is.EqualTo(3));
            Assert.That(result.Description, Is.EqualTo("Reports TestBot stats."));
        }
    }

    [Test]
    public async Task CheckHealthAsync_WhenChatCountFails_ReturnsUnhealthyResult()
    {
        var databaseService = Substitute.For<ITestBotDatabaseService>();
        var exception = new InvalidOperationException("boom");
        databaseService.GetChatCount().Returns(Task.FromException<long>(exception));
        var healthCheck = new BotStatsHealthCheck(databaseService, "TestBot");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
            Assert.That(result.Exception, Is.SameAs(exception));
            Assert.That(result.Description, Is.EqualTo("Reports TestBot stats."));
        }
    }
}
