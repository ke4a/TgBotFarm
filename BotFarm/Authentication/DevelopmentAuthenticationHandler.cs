using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BotFarm.Authentication;

/// <summary>
/// Constants for the development-only authentication scheme.
/// </summary>
public static class DevelopmentAuthenticationDefaults
{
    public const string Scheme = "Development";
}

/// <summary>
/// Automatically authenticates a local developer identity when the host runs in development.
/// </summary>
public class DevelopmentAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Creates the development authentication handler.
    /// </summary>
    public DevelopmentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <summary>
    /// Returns a synthetic authenticated principal for local development sessions.
    /// </summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "developer")
            ],
            DevelopmentAuthenticationDefaults.Scheme);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DevelopmentAuthenticationDefaults.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
