using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using TestBot.Abstractions;
using TestBot.Health;

namespace TestBot.UnitTests.Health;

[TestFixture]
public class TestBotStatsHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenChatIdsLoad_ReturnsHealthyResultWithChatCount()
    {
        var databaseService = Substitute.For<ITestBotDatabaseService>();
        databaseService.GetChatCount().Returns(Task.FromResult(3L));
        var healthCheck = new TestBotStatsHealthCheck(databaseService);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(result.Data["ChatsCount"], Is.EqualTo(3));
        }
    }

    [Test]
    public async Task CheckHealthAsync_WhenChatIdsFail_ReturnsUnhealthyResult()
    {
        var databaseService = Substitute.For<ITestBotDatabaseService>();
        databaseService.GetChatCount().Returns(Task.FromException<long>(new InvalidOperationException("boom")));
        var healthCheck = new TestBotStatsHealthCheck(databaseService);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
            Assert.That(result.Exception, Is.Not.Null);
        }
    }
}
