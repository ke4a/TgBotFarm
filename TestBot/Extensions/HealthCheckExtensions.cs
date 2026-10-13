using Microsoft.Extensions.DependencyInjection;
using BotFarm.Core.Health;
using TestBot.Abstractions;

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
        this         IServiceCollection services)
    {
        var builder = services.AddHealthChecks();
        builder.AddBotStatsHealthCheck<ITestBotDatabaseService>(Constants.Name);

        return services;
    }
}
