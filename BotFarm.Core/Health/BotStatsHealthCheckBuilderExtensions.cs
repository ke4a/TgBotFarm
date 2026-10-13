using BotFarm.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.Core.Health;

/// <summary>
/// Registration helpers for bot statistics health checks.
/// </summary>
public static class BotStatsHealthCheckBuilderExtensions
{
    /// <summary>
    /// Adds a health check that reports the chat count for a bot.
    /// </summary>
    /// <typeparam name="TDatabaseService">The bot's database service type.</typeparam>
    /// <param name="builder">The health-check builder.</param>
    /// <param name="botName">The bot name used for the check name, tag, and description.</param>
    /// <returns>The health-check builder.</returns>
    public static IHealthChecksBuilder AddBotStatsHealthCheck<TDatabaseService>(
        this IHealthChecksBuilder builder,
        string botName)
        where TDatabaseService : IDatabaseService
    {
        builder.Add(new HealthCheckRegistration(
            $"{botName}Stats",
            serviceProvider => new BotStatsHealthCheck(
                serviceProvider.GetRequiredService<TDatabaseService>(),
                botName),
            HealthStatus.Unhealthy,
            [botName]));

        return builder;
    }
}
