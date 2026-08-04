using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.Health;

/// <summary>
/// Reports lightweight application-level health data.
/// </summary>
public class AppStatsHealthCheck : IHealthCheck
{
    /// <summary>
    /// Creates the application stats health check.
    /// </summary>
    public AppStatsHealthCheck()
    {
    }

    /// <summary>
    /// Returns a healthy result containing the current application uptime.
    /// </summary>
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
