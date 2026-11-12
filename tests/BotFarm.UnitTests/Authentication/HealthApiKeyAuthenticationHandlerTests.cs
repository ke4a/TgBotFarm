using System.Text.Encodings.Web;
using BotFarm.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.UnitTests.Authentication;

/// <summary>
/// Checks how the health API key handler treats each kind of request, and how its key setting is validated.
/// </summary>
[TestFixture]
public class HealthApiKeyAuthenticationHandlerTests
{
    private const string ValidKey = "0123456789abcdef0123456789abcdef";

    private static async Task<AuthenticateResult> Authenticate(string? authorization, string configuredKey = ValidKey)
    {
        var options = new HealthApiKeyAuthenticationOptions { ApiKey = configuredKey };
        var optionsMonitor = Substitute.For<IOptionsMonitor<HealthApiKeyAuthenticationOptions>>();
        optionsMonitor.Get(Arg.Any<string>()).Returns(options);

        var handler = new HealthApiKeyAuthenticationHandler(optionsMonitor, NullLoggerFactory.Instance, UrlEncoder.Default);
        var httpContext = new DefaultHttpContext();
        if (authorization is not null)
        {
            httpContext.Request.Headers.Authorization = authorization;
        }

        var scheme = new AuthenticationScheme(
            HealthApiKeyAuthenticationDefaults.Scheme, null, typeof(HealthApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, httpContext);

        return await handler.AuthenticateAsync();
    }

    [TestCase(null)]
    [TestCase("Basic dXNlcjpwYXNz")]
    public async Task Authenticate_WithoutBearerToken_ReturnsNoResult(string? authorization)
    {
        var result = await Authenticate(authorization);

        Assert.That(result.None, Is.True);
    }

    [Test]
    public async Task Authenticate_WhenNoKeyIsConfigured_ReturnsNoResultEvenWithAToken()
    {
        var result = await Authenticate($"Bearer {ValidKey}", configuredKey: "");

        Assert.That(result.None, Is.True);
    }

    [Test]
    public async Task Authenticate_WithWrongKey_Fails()
    {
        var result = await Authenticate("Bearer wrong-key");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure?.Message, Is.EqualTo("Invalid health API key."));
        }
    }

    [TestCase("Bearer " + ValidKey)]
    [TestCase("bearer " + ValidKey)]
    public async Task Authenticate_WithConfiguredKey_Succeeds(string authorization)
    {
        var result = await Authenticate(authorization);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Principal!.Identity!.AuthenticationType, Is.EqualTo(HealthApiKeyAuthenticationDefaults.Scheme));
        }
    }

    [TestCase("", true)]
    [TestCase("short-key", false)]
    [TestCase(ValidKey, true)]
    public void Validator_AcceptsEmptyOrLongEnoughKeys(string key, bool expectedValid)
    {
        var result = new HealthApiKeyAuthenticationOptionsValidator().Validate(
            HealthApiKeyAuthenticationDefaults.Scheme, new HealthApiKeyAuthenticationOptions { ApiKey = key });

        Assert.That(result.Succeeded, Is.EqualTo(expectedValid));
    }
}
