using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BotFarm.Authentication;

/// <summary>
/// Constants for the API key scheme that lets uptime monitors read the <c>/health</c> endpoint.
/// </summary>
public static class HealthApiKeyAuthenticationDefaults
{
    public const string Scheme = "HealthApiKey";

    /// <summary>
    /// Configuration key for the API key (env var <c>HealthChecks__ApiKey</c>).
    /// </summary>
    public const string ConfigurationKey = "HealthChecks:ApiKey";

    /// <summary>
    /// The shortest API key accepted. Shorter keys stop startup.
    /// </summary>
    public const int MinimumKeyLength = 32;
}

/// <summary>
/// Options for <see cref="HealthApiKeyAuthenticationHandler"/>.
/// </summary>
public class HealthApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The secret an uptime monitor sends as <c>Authorization: Bearer &lt;key&gt;</c>. Empty turns API key access
    /// off.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Rejects an API key that is set but shorter than <see cref="HealthApiKeyAuthenticationDefaults.MinimumKeyLength"/>.
/// </summary>
public class HealthApiKeyAuthenticationOptionsValidator : IValidateOptions<HealthApiKeyAuthenticationOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, HealthApiKeyAuthenticationOptions options)
    {
        if (name != HealthApiKeyAuthenticationDefaults.Scheme
            || options.ApiKey.Length == 0
            || options.ApiKey.Length >= HealthApiKeyAuthenticationDefaults.MinimumKeyLength)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{HealthApiKeyAuthenticationDefaults.ConfigurationKey} must be empty or at least " +
            $"{HealthApiKeyAuthenticationDefaults.MinimumKeyLength} characters long.");
    }
}

/// <summary>
/// Signs in a request that sends the configured health API key as <c>Authorization: Bearer &lt;key&gt;</c>.
/// </summary>
/// <remarks>
/// The resulting identity satisfies only the policy for the <c>/health</c> endpoint. Admin pages accept only the
/// admin sign-in, so the key gives no access to them.
/// </remarks>
public class HealthApiKeyAuthenticationHandler : AuthenticationHandler<HealthApiKeyAuthenticationOptions>
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Creates the handler.
    /// </summary>
    public HealthApiKeyAuthenticationHandler(
        IOptionsMonitor<HealthApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <summary>
    /// Returns no result when no key is configured or the request has no bearer token, fails on a wrong key, and
    /// succeeds on the configured key. Keys are compared in constant time.
    /// </summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (string.IsNullOrEmpty(Options.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedKey = header[BearerPrefix.Length..].Trim();
        if (!KeysMatch(providedKey, Options.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid health API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "health-monitor")],
            HealthApiKeyAuthenticationDefaults.Scheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), HealthApiKeyAuthenticationDefaults.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Answers with 401 and <c>WWW-Authenticate: Bearer</c>.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    // Hashing first gives both sides the same length, so the comparison time does not reveal the key length.
    private static bool KeysMatch(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
