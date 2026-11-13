using System.Net;
using BotFarm.IntegrationTests.Support;

namespace BotFarm.IntegrationTests.Host;

/// <summary>
/// Signs an existing admin in and out through the account pages and the dashboard's sign-out form.
/// </summary>
[TestFixture]
public class AdminSignInTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Integration#Pass1";

    private BotFarmAppFactory _app = null!;

    [OneTimeSetUp]
    public async Task StartApp()
    {
        _app = await BotFarmAppFactory.CreateAsync();
        await _app.CreateAdminAsync(AdminUserName, AdminPassword);
    }

    [OneTimeTearDown]
    public async Task StopApp()
    {
        await _app.DisposeAsync();
    }

    [Test]
    public async Task SignIn_WithAWrongPassword_ShowsAnErrorAndKeepsTheAdminSignedOut()
    {
        using var browser = _app.CreateBrowser();

        using var response = await SignInAsync(browser, "Wrong#Pass1");

        var html = await response.Content.ReadAsStringAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(html, Does.Contain("account-error"));
        }

        using var dashboard = await browser.GetAsync("/");
        Assert.That(dashboard.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    [Test]
    public async Task SignIn_WithThePassword_OpensTheDashboardAndReturnUrl()
    {
        using var browser = _app.CreateBrowser();

        using var response = await SignInAsync(browser, AdminPassword, returnUrl: "/logs");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/logs"));
        }

        using var dashboard = await browser.GetAsync("/");
        Assert.That(dashboard.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SignIn_WithAnExternalReturnUrl_DoesNotRedirectThere()
    {
        using var browser = _app.CreateBrowser();

        HttpResponseMessage? response = null;
        try
        {
            response = await SignInAsync(browser, AdminPassword, returnUrl: "https://attacker.test/");
        }
        catch (InvalidOperationException)
        {
            // The in-memory server passes the app's refusal on as the exception it threw.
        }

        using (response)
        {
            Assert.That(response?.Headers.Location?.IsAbsoluteUri, Is.Not.True);
        }
    }

    [Test]
    public async Task SignOut_WithoutTheAntiforgeryToken_IsRejectedAndKeepsTheSession()
    {
        using var browser = _app.CreateBrowser();
        (await SignInAsync(browser, AdminPassword)).Dispose();

        using var response = await browser.PostAsync("/Account/Logout", new FormUrlEncodedContent([]));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var dashboard = await browser.GetAsync("/");
        Assert.That(dashboard.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SignOut_FromTheDashboardForm_EndsTheSession()
    {
        using var browser = _app.CreateBrowser();
        (await SignInAsync(browser, AdminPassword)).Dispose();
        var dashboard = await browser.GetStringAsync("/");

        using var response = await browser.PostAsync(
            "/Account/Logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = BotFarmAppFactory.GetAntiforgeryToken(dashboard)
            }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/Account/Login"));
        }

        using var afterSignOut = await browser.GetAsync("/");
        Assert.That(afterSignOut.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient browser, string password, string? returnUrl = null)
    {
        var page = await browser.GetStringAsync("/Account/Login");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = BotFarmAppFactory.GetAntiforgeryToken(page),
            ["UserName"] = AdminUserName,
            ["Password"] = password,
            ["ReturnUrl"] = returnUrl ?? string.Empty
        });

        return await browser.PostAsync("/Account/Login", form);
    }
}
