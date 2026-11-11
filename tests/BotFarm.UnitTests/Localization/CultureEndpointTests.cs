using System.Text.Encodings.Web;
using BotFarm.Localization;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace BotFarm.UnitTests.Localization;

/// <summary>
/// Calls <c>GET /culture/set</c> on a pipeline that requires authentication everywhere else, and checks the
/// language cookie it writes and where it redirects.
/// </summary>
[TestFixture]
public class CultureEndpointTests
{
    private const string CultureCookieName = ".AspNetCore.Culture";
    private const string RussianCookieValue = "c%3Dru-RU%7Cuic%3Dru-RU";

    private IHost _host = default!;
    private TestServer _server = default!;

    [SetUp]
    public async Task SetUp()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAdminLocalizationForTests();
                    services.AddAuthentication(RejectingAuthenticationHandler.Scheme)
                        .AddScheme<AuthenticationSchemeOptions, RejectingAuthenticationHandler>(
                            RejectingAuthenticationHandler.Scheme, _ => { });
                    services.AddAuthorizationBuilder()
                        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapAdminLanguageEndpoints();
                        endpoints.MapGet("/protected", () => "ok");
                    });
                }))
            .StartAsync();
        _server = _host.GetTestServer();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [TestCase("ru-RU")]
    [TestCase("RU-ru")]
    [TestCase("ru-ru")]
    public async Task SupportedCulture_WritesCultureCookie(string culture)
    {
        // Act
        var context = await GetAsync($"?culture={culture}&returnUrl=%2Fdashboard");

        // Assert
        var cookie = CultureCookie(context);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status302Found));
            Assert.That(cookie, Is.Not.Null);
            Assert.That(cookie!.Value.Value, Is.EqualTo(RussianCookieValue));
            Assert.That(cookie.MaxAge, Is.EqualTo(TimeSpan.FromSeconds(31536000)));
            Assert.That(cookie.Path.Value, Is.EqualTo("/"));
            Assert.That(cookie.HttpOnly, Is.True);
            Assert.That(cookie.SameSite, Is.EqualTo(Microsoft.Net.Http.Headers.SameSiteMode.Lax));
            Assert.That(cookie.Secure, Is.False);
        }
    }

    [Test]
    public async Task SupportedCulture_OverHttps_WritesSecureCookie()
    {
        // Act
        var context = await GetAsync("?culture=uk-UA", https: true);

        // Assert
        var cookie = CultureCookie(context);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cookie, Is.Not.Null);
            Assert.That(cookie!.Value.Value, Is.EqualTo("c%3Duk-UA%7Cuic%3Duk-UA"));
            Assert.That(cookie.Secure, Is.True);
        }
    }

    [TestCase("?culture=de-DE&returnUrl=%2Fdashboard")]
    [TestCase("?culture=not-a-culture&returnUrl=%2Fdashboard")]
    [TestCase("?culture=&returnUrl=%2Fdashboard")]
    [TestCase("?returnUrl=%2Fdashboard")]
    [TestCase("")]
    public async Task UnsupportedOrMissingCulture_WritesNoCookieAndRedirects(string query)
    {
        // Act
        var context = await GetAsync(query);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status302Found));
            Assert.That(context.Response.Headers.SetCookie, Is.Empty);
        }
    }

    [TestCase("/dashboard?x=1", "/dashboard?x=1")]
    [TestCase("/", "/")]
    [TestCase("~/testbot/dashboard", "/testbot/dashboard")]
    public async Task LocalReturnUrl_IsRedirectTarget(string returnUrl, string expectedLocation)
    {
        // Act
        var context = await GetAsync($"?culture=ru-RU&returnUrl={Uri.EscapeDataString(returnUrl)}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status302Found));
            Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo(expectedLocation));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("https://example.com")]
    [TestCase("//example.com")]
    [TestCase("/\\example.com")]
    [TestCase("~//example.com")]
    [TestCase("dashboard")]
    public async Task MissingOrNonLocalReturnUrl_RedirectsToRoot(string? returnUrl)
    {
        // Arrange
        var query = returnUrl is null ? "?culture=ru-RU" : $"?culture=ru-RU&returnUrl={Uri.EscapeDataString(returnUrl)}";

        // Act
        var context = await GetAsync(query);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status302Found));
            Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/"));
        }
    }

    [Test]
    public async Task OtherEndpoint_RequiresAuthentication()
    {
        // Act
        var context = await _server.SendAsync(c =>
        {
            c.Request.Method = HttpMethods.Get;
            c.Request.Path = "/protected";
        });

        // Assert
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
    }

    [Test]
    public async Task SetCulture_WithoutAuthentication_IsNotChallenged()
    {
        // Act
        var context = await GetAsync("?culture=ru-RU&returnUrl=%2Fdashboard");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status302Found));
            Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/dashboard"));
        }
    }

    private Task<HttpContext> GetAsync(string query, bool https = false) =>
        _server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = https ? "https" : "http";
            context.Request.Path = "/culture/set";
            context.Request.QueryString = new QueryString(query);
        });

    private static SetCookieHeaderValue? CultureCookie(HttpContext context) =>
        SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.ToArray())
            .SingleOrDefault(cookie => cookie.Name.Value == CultureCookieName);

    /// <summary>
    /// Authenticates nobody, so every endpoint without <c>AllowAnonymous</c> is challenged with 401.
    /// </summary>
    private sealed class RejectingAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Rejecting";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
