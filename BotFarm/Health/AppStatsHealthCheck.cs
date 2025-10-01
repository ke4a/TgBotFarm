using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.Health;

public class AppStatsHealthCheck : IHealthCheck
{
    public AppStatsHealthCheck()
    {
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var uptime = DateTime.UtcNow.Subtract(Program.StartTime).ToString(@"dd\.hh\:mm\:ss");
        var data = new Dictionary<string, object>()
        {
            { "Uptime", uptime },
        };
        var status = HealthStatus.Healthy;

        return Task.FromResult(new HealthCheckResult(
            status,
            description: "Reports application stats.",
            exception: null,
            data: data));
    }
}
