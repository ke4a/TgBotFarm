using BotFarm.Authentication;
using BotFarm.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.Extensions;

/// <summary>
/// Registers the host application's health checks and HealthChecks UI.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds BotFarm health checks plus the protected HealthChecks UI endpoint configuration.
    /// </summary>
    public static IServiceCollection ConfigureHealthChecks(
        this IServiceCollection services,
        string internalApiKey)
    {
        var builder = services.AddHealthChecks();
        builder.AddCheck<MemoryHealthCheck>("MemoryCheck", HealthStatus.Unhealthy, ["BotFarmHealth"])
               .AddCheck<AppStatsHealthCheck>("AppStats", HealthStatus.Unhealthy, ["BotFarmHealth"]);

        services.AddTransient<LocalhostRedirectHandler>();
        services.AddHealthChecksUI(opt =>
        {
            opt.SetEvaluationTimeInSeconds(300); //time in seconds between check    
            opt.MaximumHistoryEntriesPerEndpoint(60); //maximum history of checks    
            opt.SetApiMaxActiveRequests(3); //api requests concurrency    
            opt.AddHealthCheckEndpoint("Health endpoint", "/health"); //map health check api
            opt.ConfigureApiEndpointHttpclient((sp, client) =>
            {
                client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, internalApiKey);
            });
            opt.UseApiEndpointDelegatingHandler<LocalhostRedirectHandler>();
        })
        .AddInMemoryStorage();

        return services;
    }
}

// https://medium.com/@jeslurrahman/implementing-health-checks-in-net-8-c3ba10af83c3