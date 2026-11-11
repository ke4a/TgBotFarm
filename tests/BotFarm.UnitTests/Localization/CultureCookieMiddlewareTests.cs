using BotFarm.Core.Extensions;
using BotFarm.Localization;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BotFarm.UnitTests.Localization;

/// <summary>
/// Sends requests through a pipeline that serves static files and then runs admin localization, and checks
/// when the language cookie is written and with which culture.
/// </summary>
[TestFixture]
public class CultureCookieMiddlewareTests
{
    private const string CultureCookieName = ".AspNetCore.Culture";
    private const string HtmlAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
    private const string StaticFileName = "app.css";

    private TempDirectoryScope _webRoot = default!;
    private IHost _host = default!;
    private HttpClient _client = default!;

    [SetUp]
    public async Task SetUp()
    {
        _webRoot = new TempDirectoryScope();
        await File.WriteAllTextAsync(_webRoot.GetPath(StaticFileName), "body {}");

        _host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddAdminLocalizationForTests();
                })
                .Configure(app =>
                {
                    app.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = new PhysicalFileProvider(_webRoot.RootPath),
                    });
                    app.UseAdminLocalization();
                    app.Run(context => context.Response.WriteAsync("ok"));
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
        _webRoot.Dispose();
    }

    [TestCase("uk-UA", "uk-UA")]
    [TestCase("de", "en-US")]
    public async Task DocumentRequestWithoutCookie_StoresDetectedCulture(string acceptLanguage, string expected)
    {
        // Act
        using var response = await SendAsync("GET", "/dashboard", acceptLanguage: acceptLanguage);

        // Assert
        Assert.That(CultureSetCookies(response), Is.EqualTo(new[] { ExpectedCookie(expected) }));
    }

    [Test]
    public async Task DocumentRequestWithUnsupportedCookie_ReplacesItWithDetectedCulture()
    {
        // Act
        using var response = await SendAsync("GET", "/dashboard", cookieCulture: "xx-XX", acceptLanguage: "uk-UA");

        // Assert
        Assert.That(CultureSetCookies(response), Is.EqualTo(new[] { ExpectedCookie("uk-UA") }));
    }

    [Test]
    public async Task DocumentRequestWithSupportedCookie_KeepsItsCultureOverAcceptLanguage()
    {
        // Act
        using var response = await SendAsync("GET", "/dashboard", cookieCulture: "ru-RU", acceptLanguage: "uk-UA");

        // Assert
        Assert.That(CultureSetCookies(response), Is.EqualTo(new[] { ExpectedCookie("ru-RU") }));
    }

    [Test]
    public async Task DocumentRequestWithSupportedCookie_RenewsItForOneYear()
    {
        // Act
        using var response = await SendAsync("GET", "/", cookieCulture: "ru-RU");

        // Assert
        var cookie = CultureSetCookies(response).Single();
        Assert.That(cookie, Does.Contain("max-age=31536000"));
    }

    [TestCase("POST", "/dashboard", HtmlAccept)]
    [TestCase("GET", "/dashboard", "application/json")]
    [TestCase("GET", "/dashboard", null)]
    [TestCase("POST", "/_blazor/negotiate?negotiateVersion=1", "*/*")]
    [TestCase("GET", "/" + StaticFileName, HtmlAccept)]
    [TestCase("GET", "/api/dashboard/chats", HtmlAccept)]
    [TestCase("GET", "/health", HtmlAccept)]
    public async Task NonDocumentRequest_GetsNoCultureCookie(string method, string path, string? accept)
    {
        // Act
        using var response = await SendAsync(method, path, accept: accept, acceptLanguage: "uk-UA");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.IsSuccessStatusCode, Is.True);
            Assert.That(CultureSetCookies(response), Is.Empty);
        }
    }

    [Test]
    public async Task StaticFileRequest_IsServedByStaticFiles()
    {
        // Act
        using var response = await SendAsync("GET", "/" + StaticFileName, acceptLanguage: "uk-UA");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(body, Is.EqualTo("body {}"));
    }

    [TestCase("http://localhost/dashboard", false)]
    [TestCase("https://localhost/dashboard", true)]
    public async Task DocumentRequest_MarksCookieSecureOnlyOverHttps(string url, bool secure)
    {
        // Act
        using var response = await SendAsync("GET", url, acceptLanguage: "uk-UA");

        // Assert
        var cookie = CultureSetCookies(response).Single();
        Assert.That(cookie.Contains("; secure", StringComparison.Ordinal), Is.EqualTo(secure));
    }

    private static string ExpectedCookie(string culture) =>
        $"{CultureCookieName}=c%3D{culture}%7Cuic%3D{culture}; max-age=31536000; path=/; samesite=lax; httponly";

    private Task<HttpResponseMessage> SendAsync(
        string method,
        string url,
        string? accept = HtmlAccept,
        string? acceptLanguage = null,
        string? cookieCulture = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        if (cookieCulture is not null)
        {
            request.Headers.Add("Cookie", $"{CultureCookieName}=c={cookieCulture}|uic={cookieCulture}");
        }

        return _client.SendAsync(request);
    }

    private static List<string> CultureSetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(value => value.StartsWith(CultureCookieName, StringComparison.Ordinal)).ToList()
            : [];
}
