using BotFarm.Authentication;
using BotFarm.Health;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BotFarm.Extensions;

/// <summary>
/// Registers the host application's health checks, the timer that runs them, and access to the <c>/health</c>
/// endpoint.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// How often every health check runs in the background.
    /// </summary>
    public static readonly TimeSpan PublishPeriod = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Path of the endpoint that returns every health check result as JSON.
    /// </summary>
    public const string HealthPath = "/health";

    /// <summary>
    /// Authorization policy for the <c>/health</c> endpoint: a signed-in admin or a request with the health API key.
    /// </summary>
    public const string HealthReaderPolicy = "HealthReader";

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

    /// <summary>
    /// Adds the health API key scheme, reading its key from <c>HealthChecks:ApiKey</c> and checking it at startup,
    /// and the <see cref="HealthReaderPolicy"/> that accepts either <paramref name="userScheme"/> or that key.
    /// </summary>
    /// <param name="authentication">The app's authentication builder.</param>
    /// <param name="configuration">Configuration that holds the key.</param>
    /// <param name="userScheme">The scheme admins sign in with.</param>
    public static AuthenticationBuilder AddHealthApiKey(
        this AuthenticationBuilder authentication,
        IConfiguration configuration,
        string userScheme)
    {
        var services = authentication.Services;
        services.AddOptions<HealthApiKeyAuthenticationOptions>(HealthApiKeyAuthenticationDefaults.Scheme)
            .Configure(options =>
                options.ApiKey = configuration[HealthApiKeyAuthenticationDefaults.ConfigurationKey] ?? string.Empty)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<HealthApiKeyAuthenticationOptions>,
            HealthApiKeyAuthenticationOptionsValidator>();

        services.AddAuthorizationBuilder()
            .AddPolicy(HealthReaderPolicy, policy => policy
                .AddAuthenticationSchemes(userScheme, HealthApiKeyAuthenticationDefaults.Scheme)
                .RequireAuthenticatedUser());

        return authentication.AddScheme<HealthApiKeyAuthenticationOptions, HealthApiKeyAuthenticationHandler>(
            HealthApiKeyAuthenticationDefaults.Scheme, _ => { });
    }

    /// <summary>
    /// Makes the admin cookie answer a signed-out request to <see cref="HealthPath"/> with 401 instead of a redirect
    /// to the sign-in page, so an uptime monitor with a missing or wrong key sees a failure, not the sign-in page.
    /// Other paths still redirect.
    /// </summary>
    public static void AnswerHealthWithUnauthorized(this CookieAuthenticationOptions options)
    {
        var redirectToLogin = options.Events.OnRedirectToLogin;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            return redirectToLogin(context);
        };
    }
}
