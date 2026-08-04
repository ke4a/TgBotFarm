using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TestBot.Health;

namespace TestBot.Extensions;

/// <summary>
/// Registers health checks specific to the reference TestBot implementation.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds the TestBot statistics health check to the shared health-check pipeline.
    /// </summary>
    public static IServiceCollection AddTestBotHealthChecks(
        this IServiceCollection services)
    {
        var builder = services.AddHealthChecks();
        builder.AddCheck<TestBotStatsHealthCheck>($"{Constants.Name}Stats", HealthStatus.Unhealthy, [Constants.Name]);

        return services;
    }
}
