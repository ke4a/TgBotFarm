using System.Net;
using System.Net.Http.Headers;
using BotFarm.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BotFarm.UnitTests.Extensions;

/// <summary>
/// Runs a pipeline with the admin cookie and the health API key the way the app sets them up, and checks who can
/// read <c>/health</c> and that the key does not open admin pages.
/// </summary>
[TestFixture]
public class HealthEndpointAuthorizationTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    private static async Task<IHost> StartHost(string apiKey)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["HealthChecks:ApiKey"] = apiKey }))
                .ConfigureServices((context, services) =>
                {
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddAuthentication(IdentityConstants.ApplicationScheme)
                        .AddCookie(IdentityConstants.ApplicationScheme, options =>
                        {
                            options.LoginPath = "/Account/Login";
                            options.AnswerHealthWithUnauthorized();
                        })
                        .AddHealthApiKey(context.Configuration, IdentityConstants.ApplicationScheme);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet(HealthCheckExtensions.HealthPath, () => "health")
                            .RequireAuthorization(HealthCheckExtensions.HealthReaderPolicy);
                        endpoints.MapGet("/dashboard", () => "dashboard").RequireAuthorization();
                    });
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    private static HttpRequestMessage Get(string path, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    [Test]
    public async Task Health_WithConfiguredKey_IsAllowed()
    {
        using var host = await StartHost(Key);
        using var response = await host.GetTestClient().SendAsync(Get("/health", Key));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase(null)]
    [TestCase("wrong-key")]
    public async Task Health_WithoutValidKey_Returns401InsteadOfSignInRedirect(string? bearer)
    {
        using var host = await StartHost(Key);
        using var response = await host.GetTestClient().SendAsync(Get("/health", bearer));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.Contain("Bearer"));
        }
    }

    [Test]
    public async Task Health_WhenNoKeyIsConfigured_RejectsAnyToken()
    {
        using var host = await StartHost("");
        using var response = await host.GetTestClient().SendAsync(Get("/health", Key));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AdminPage_WithConfiguredKey_StillRedirectsToSignIn()
    {
        using var host = await StartHost(Key);
        using var response = await host.GetTestClient().SendAsync(Get("/dashboard", Key));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo("/Account/Login"));
        }
    }

    [Test]
    public async Task Startup_WithTooShortKey_Fails()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => StartHost("short-key"));

        Assert.That(exception!.Message, Does.Contain("HealthChecks:ApiKey"));
    }
}
