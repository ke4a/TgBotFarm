using BotFarm.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.UnitTests.Health;

[TestFixture]
public class AppStatsHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_ReturnsHealthyStatusAndUptime()
    {
        var healthCheck = new AppStatsHealthCheck();

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(result.Description, Is.EqualTo("Reports application stats."));
            Assert.That(result.Data, Does.ContainKey("Uptime"));
            Assert.That(result.Data["Uptime"], Is.TypeOf<string>());
        }
    }
}
