using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TestBot.Health;

namespace TestBot.Extensions;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddTestBotHealthChecks(
        this IServiceCollection services)
    {
        var builder = services.AddHealthChecks();
        builder.AddCheck<TestBotStatsHealthCheck>($"{Constants.Name}Stats", HealthStatus.Unhealthy, [Constants.Name]);

        return services;
    }
}
