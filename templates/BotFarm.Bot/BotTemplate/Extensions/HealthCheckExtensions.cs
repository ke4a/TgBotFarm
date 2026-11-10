using BotFarm.Core.Health;
using Microsoft.Extensions.DependencyInjection;
using BotTemplate.Abstractions;

namespace BotTemplate.Extensions;

/// <summary>
/// Registers health checks specific to BotTemplate.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds the BotTemplate statistics health check to the shared health-check pipeline.
    /// </summary>
    public static IServiceCollection AddBotTemplateHealthChecks(this IServiceCollection services)
    {
        var builder = services.AddHealthChecks();
        builder.AddBotStatsHealthCheck<IBotTemplateDatabaseService>(Constants.Name);

        return services;
    }
}
