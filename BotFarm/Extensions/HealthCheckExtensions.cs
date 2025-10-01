using BotFarm.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.Http.Headers;
using System.Text;

namespace BotFarm.Extensions;

public static class HealthCheckExtensions
{
    public static IServiceCollection ConfigureHealthChecks(
        this IServiceCollection services,
        IConfiguration config)
    {
        var builder = services.AddHealthChecks();
        builder.AddCheck<MemoryHealthCheck>("MemoryCheck", HealthStatus.Unhealthy, ["BotFarmHealth"])
               .AddCheck<AppStatsHealthCheck>("AppStats", HealthStatus.Unhealthy, ["BotFarmHealth"]);

        var authString = $"{config["AuthenticationConfig:AdminUser"]}:{config["AuthenticationConfig:AdminPassword"]}";
        var base64EncodedAuthString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authString));
        services.AddHealthChecksUI(opt =>
        {
            opt.SetEvaluationTimeInSeconds(120); //time in seconds between check    
            opt.MaximumHistoryEntriesPerEndpoint(60); //maximum history of checks    
            opt.SetApiMaxActiveRequests(3); //api requests concurrency    
            opt.AddHealthCheckEndpoint("Health endpoint", "/health"); //map health check api
            opt.ConfigureApiEndpointHttpclient((sp, client) =>
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthString); ;
            });
        })
        .AddInMemoryStorage();

        return services;
    }
}

// https://medium.com/@jeslurrahman/implementing-health-checks-in-net-8-c3ba10af83c3