using System.Text.Encodings.Web;
using BotFarm.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.UnitTests.Authentication;

[TestFixture]
public class DevelopmentAuthenticationHandlerTests
{
    [Test]
    public async Task Authenticate_ReturnsDeveloperIdentity()
    {
        var options = new AuthenticationSchemeOptions();
        var optionsMonitor = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.CurrentValue.Returns(options);
        optionsMonitor.Get(Arg.Any<string>()).Returns(options);
        var handler = new DevelopmentAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);
        var scheme = new AuthenticationScheme(
            DevelopmentAuthenticationDefaults.Scheme,
            DevelopmentAuthenticationDefaults.Scheme,
            typeof(DevelopmentAuthenticationHandler));

        await handler.InitializeAsync(scheme, new DefaultHttpContext());
        var result = await handler.AuthenticateAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("developer"));
            Assert.That(
                result.Principal?.Identity?.AuthenticationType,
                Is.EqualTo(DevelopmentAuthenticationDefaults.Scheme));
            Assert.That(result.Ticket?.AuthenticationScheme, Is.EqualTo(DevelopmentAuthenticationDefaults.Scheme));
        }
    }
}
