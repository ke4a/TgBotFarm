using BotFarm.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.Extensions;

/// <summary>
/// Registers the host application's health checks and the timer that runs them.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// How often every health check runs in the background.
    /// </summary>
    public static readonly TimeSpan PublishPeriod = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Adds the BotFarm memory and app stats checks, and a publisher that runs all registered checks every
    /// <see cref="PublishPeriod"/> and logs the result.
    /// </summary>
    public static IServiceCollection ConfigureHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<MemoryHealthCheck>("MemoryCheck", HealthStatus.Unhealthy, ["BotFarmHealth"])
            .AddCheck<AppStatsHealthCheck>("AppStats", HealthStatus.Unhealthy, ["BotFarmHealth"]);

        services.Configure<HealthCheckPublisherOptions>(options => options.Period = PublishPeriod);
        services.AddSingleton<IHealthCheckPublisher, HealthReportLogPublisher>();

        return services;
    }
}
