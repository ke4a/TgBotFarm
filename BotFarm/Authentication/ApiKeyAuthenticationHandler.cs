using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BotFarm.Authentication;

/// <summary>
/// Constants for the internal API key authentication scheme.
/// </summary>
public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";

    public const string HeaderName = "X-Api-Key";
}

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Shared secret generated once per process lifetime, used only for internal service-to-service calls
    /// (e.g. the Health Checks UI polling its own /health endpoint). Never exposed externally.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Lightweight authentication scheme intended solely for internal, machine-to-machine requests
/// (e.g. the Health Checks UI calling back into the /health endpoint). Not for interactive users.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    /// <summary>
    /// Creates the handler used for internal service-to-service requests.
    /// </summary>
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <summary>
    /// Authenticates requests that present the process-local internal API key header.
    /// </summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var providedKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (string.IsNullOrEmpty(Options.ApiKey) || providedKey != Options.ApiKey)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "internal-service")
            ],
            ApiKeyAuthenticationDefaults.Scheme);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
