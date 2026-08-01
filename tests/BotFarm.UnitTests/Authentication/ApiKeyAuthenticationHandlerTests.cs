using System.Text.Encodings.Web;
using BotFarm.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.UnitTests.Authentication;

[TestFixture]
public class ApiKeyAuthenticationHandlerTests
{
    private const string ValidApiKey = "super-secret-key";

    private static async Task<AuthenticateResult> AuthenticateAsync(string? providedApiKey, string configuredApiKey = ValidApiKey)
    {
        var options = new ApiKeyAuthenticationOptions { ApiKey = configuredApiKey };

        var optionsMonitor = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        optionsMonitor.CurrentValue.Returns(options);
        optionsMonitor.Get(Arg.Any<string>()).Returns(options);

        var handler = new ApiKeyAuthenticationHandler(optionsMonitor, NullLoggerFactory.Instance, UrlEncoder.Default);

        var httpContext = new DefaultHttpContext();
        if (providedApiKey is not null)
        {
            httpContext.Request.Headers[ApiKeyAuthenticationDefaults.HeaderName] = providedApiKey;
        }

        var scheme = new AuthenticationScheme(
            ApiKeyAuthenticationDefaults.Scheme,
            ApiKeyAuthenticationDefaults.Scheme,
            typeof(ApiKeyAuthenticationHandler));

        await handler.InitializeAsync(scheme, httpContext);

        return await handler.AuthenticateAsync();
    }

    [Test]
    public async Task AuthenticateAsync_NoApiKeyHeader_ReturnsNoResult()
    {
        var result = await AuthenticateAsync(providedApiKey: null);

        Assert.That(result.None, Is.True);
        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task AuthenticateAsync_InvalidApiKey_ReturnsFailure()
    {
        var result = await AuthenticateAsync(providedApiKey: "wrong-key");

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure?.Message, Is.EqualTo("Invalid API key."));
    }

    [Test]
    public async Task AuthenticateAsync_ConfiguredApiKeyIsEmpty_ReturnsFailure()
    {
        var result = await AuthenticateAsync(providedApiKey: "anything", configuredApiKey: string.Empty);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Failure?.Message, Is.EqualTo("Invalid API key."));
    }

    [Test]
    public async Task AuthenticateAsync_ValidApiKey_ReturnsSuccessWithInternalServiceClaim()
    {
        var result = await AuthenticateAsync(providedApiKey: ValidApiKey);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("internal-service"));
        Assert.That(result.Principal?.Identity?.AuthenticationType, Is.EqualTo(ApiKeyAuthenticationDefaults.Scheme));
        Assert.That(result.Ticket?.AuthenticationScheme, Is.EqualTo(ApiKeyAuthenticationDefaults.Scheme));
    }
}
