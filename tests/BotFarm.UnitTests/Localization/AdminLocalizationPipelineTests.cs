using System.Globalization;
using BotFarm.Core.Extensions;
using BotFarm.Localization;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BotFarm.UnitTests.Localization;

/// <summary>
/// Sends requests through a pipeline with admin localization and checks which requests take their culture
/// from the language cookie and which keep the server's default culture.
/// </summary>
[NonParallelizable]
[TestFixture]
public class AdminLocalizationPipelineTests
{
    private const string ServerCulture = "de-DE";
    private const string CookieCulture = "ru-RU";
    private const string CultureCookieName = ".AspNetCore.Culture";

    private CultureInfo? _originalDefaultCulture;
    private CultureInfo? _originalDefaultUICulture;
    private CultureScope _cultureScope = default!;
    private IHost _host = default!;
    private HttpClient _client = default!;

    [SetUp]
    public async Task SetUp()
    {
        _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        _originalDefaultUICulture = CultureInfo.DefaultThreadCurrentUICulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(ServerCulture);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(ServerCulture);
        _cultureScope = new CultureScope(ServerCulture);

        _host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAdminLocalization();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAdminLocalization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/api/TestBot/Update", WriteCulture);
                        endpoints.MapPost("/api/dashboard/shutdown", WriteCulture);
                        endpoints.MapGet("/health", WriteCulture);
                        endpoints.MapGet("/healthz", WriteCulture);
                        endpoints.MapGet("/dashboard/health", WriteCulture);
                        endpoints.MapGet("/dashboard", WriteCulture);
                    });
                }))
            .StartAsync();
        _client = _host.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _cultureScope.Dispose();
        CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUICulture;
    }

    [TestCase("POST", "/api/TestBot/Update")]
    [TestCase("POST", "/api/dashboard/shutdown")]
    [TestCase("GET", "/health")]
    [TestCase("GET", "/healthz")]
    public async Task RequestOutsideAdminUi_KeepsServerDefaultCultureAndGetsNoCultureCookie(string method, string path)
    {
        // Act
        using var response = await SendAsync(method, path);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body, Is.EqualTo($"{ServerCulture}|{ServerCulture}"));
            Assert.That(CultureSetCookies(response), Is.Empty);
        }
    }

    [TestCase("/dashboard")]
    [TestCase("/dashboard/health")]
    public async Task AdminUiRequest_RunsInTheCookieCulture(string path)
    {
        // Act
        using var response = await SendAsync("GET", path);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(body, Is.EqualTo($"{CookieCulture}|{CookieCulture}"));
    }

    [TestCase("/api", false)]
    [TestCase("/api/x", false)]
    [TestCase("/API/x", false)]
    [TestCase("/health", false)]
    [TestCase("/healthz", false)]
    [TestCase("/", true)]
    [TestCase("/dashboard", true)]
    [TestCase("/health-ui", true)]
    [TestCase("/apix", true)]
    [TestCase("/Account/Login", true)]
    public void Applies_ExcludesApiAndHealthSegmentsOnly(string path, bool expected)
    {
        // Act
        var applies = AdminLocalizationScope.Applies(new PathString(path));

        // Assert
        Assert.That(applies, Is.EqualTo(expected));
    }

    private static Task WriteCulture(HttpContext context) =>
        context.Response.WriteAsync($"{CultureInfo.CurrentCulture.Name}|{CultureInfo.CurrentUICulture.Name}");

    private Task<HttpResponseMessage> SendAsync(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Cookie", $"{CultureCookieName}=c={CookieCulture}|uic={CookieCulture}");
        request.Headers.Add("Accept-Language", "uk-UA");
        return _client.SendAsync(request);
    }

    private static List<string> CultureSetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(value => value.StartsWith(CultureCookieName, StringComparison.Ordinal)).ToList()
            : [];
}
