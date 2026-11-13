using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BotFarm.IntegrationTests.Support;

namespace BotFarm.IntegrationTests.Host;

/// <summary>
/// Sends requests through the app's real middleware pipeline: health endpoints, the TLS proxy's forwarded headers
/// and the sign-in requirement of the admin UI and API.
/// </summary>
[TestFixture]
public class HostPipelineTests
{
    private BotFarmAppFactory _app = null!;

    [OneTimeSetUp]
    public async Task StartApp()
    {
        _app = await BotFarmAppFactory.CreateAsync();
    }

    [OneTimeTearDown]
    public async Task StopApp()
    {
        await _app.DisposeAsync();
    }

    [Test]
    public async Task Liveness_AnswersAnonymousRequests()
    {
        using var client = _app.CreateBrowser();

        using var response = await client.GetAsync("/healthz");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task HealthReport_WithoutTheApiKey_AnswersUnauthorized()
    {
        using var client = _app.CreateBrowser();

        using var response = await client.GetAsync("/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task HealthReport_WithTheApiKey_ReturnsTheReportOfEveryCheck()
    {
        using var client = _app.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BotFarmAppFactory.HealthApiKey);

        using var response = await client.SendAsync(request);

        // An unhealthy check answers 503 with the same report; the test only needs the key to be accepted.
        Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable));
        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(report.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task RequestForwardedAsHttps_GetsTheHstsHeader()
    {
        using var client = _app.CreateClient(new() { BaseAddress = new Uri($"http://{BotFarmAppFactory.PublicHost}") });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.That(response.Headers.Contains("Strict-Transport-Security"), Is.True);
    }

    [Test]
    public async Task PlainHttpRequest_GetsNoHstsHeader()
    {
        using var client = _app.CreateClient(new() { BaseAddress = new Uri($"http://{BotFarmAppFactory.PublicHost}") });

        using var response = await client.GetAsync("/healthz");

        Assert.That(response.Headers.Contains("Strict-Transport-Security"), Is.False);
    }

    [Test]
    public async Task Dashboard_WhenSignedOut_RedirectsToTheSignInPage()
    {
        using var client = _app.CreateBrowser();

        using var response = await client.GetAsync("/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.AbsolutePath, Is.EqualTo("/Account/Login"));
    }

    [Test]
    public async Task DashboardApi_WhenSignedOut_DoesNotShutTheAppDown()
    {
        using var client = _app.CreateBrowser();

        using var response = await client.PostAsync("/api/dashboard/shutdown", content: null);
        using var liveness = await client.GetAsync("/healthz");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(liveness.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }
}
