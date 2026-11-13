using System.Net;
using BotFarm.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using BotFarm.IntegrationTests.Support;

namespace BotFarm.IntegrationTests.Host;

/// <summary>
/// Creates the first admin account through the setup page of an app whose identity store is empty.
/// </summary>
/// <remarks>
/// Completing setup closes the setup page, so every test starts an app of its own.
/// </remarks>
[TestFixture]
public class InitialSetupTests
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Integration#Pass1";

    private BotFarmAppFactory _app = null!;

    [SetUp]
    public async Task StartApp()
    {
        _app = await BotFarmAppFactory.CreateAsync();
    }

    [TearDown]
    public async Task StopApp()
    {
        await _app.DisposeAsync();
    }

    [Test]
    public async Task SignInPage_WhenNoAdminExists_RedirectsToSetup()
    {
        using var browser = _app.CreateBrowser();

        using var response = await browser.GetAsync("/Account/Login");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/Account/Setup"));
    }

    [Test]
    public async Task Setup_WithAWrongCode_ShowsAnErrorAndCreatesNoAdmin()
    {
        using var browser = _app.CreateBrowser();

        using var response = await SubmitSetupAsync(browser, "wrong-setup-code-0123456789");

        var html = await response.Content.ReadAsStringAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(html, Does.Contain("account-error"));
            Assert.That(await AdminCount(), Is.Zero);
        }
    }

    [Test]
    public async Task Setup_WithTheSetupCode_CreatesTheAdminAndSignsThemIn()
    {
        using var browser = _app.CreateBrowser();

        using var response = await SubmitSetupAsync(browser, BotFarmAppFactory.SetupCode);

        var authCookie = response.Headers.GetValues("Set-Cookie")
            .SingleOrDefault(cookie => cookie.StartsWith($"{BotFarmAppFactory.SignInCookieName}=", StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/"));
            Assert.That(authCookie, Is.Not.Null, "The sign-in cookie was not set.");
            Assert.That(authCookie, Does.Contain("secure").IgnoreCase);
            Assert.That(authCookie, Does.Contain("httponly").IgnoreCase);
            Assert.That(await AdminCount(), Is.EqualTo(1));
        }

        using var dashboard = await browser.GetAsync("/");
        Assert.That(dashboard.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Setup runs once: the page now sends visitors to sign in.
        using var setupPage = await _app.CreateBrowser().GetAsync("/Account/Setup");
        Assert.That(setupPage.Headers.Location?.OriginalString, Is.EqualTo("/Account/Login"));
    }

    private static async Task<HttpResponseMessage> SubmitSetupAsync(HttpClient browser, string setupCode)
    {
        var page = await browser.GetStringAsync("/Account/Setup");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = BotFarmAppFactory.GetAntiforgeryToken(page),
            ["SetupCode"] = setupCode,
            ["UserName"] = AdminUserName,
            ["Password"] = AdminPassword,
            ["ConfirmPassword"] = AdminPassword
        });

        return await browser.PostAsync("/Account/Setup", form);
    }

    private async Task<int> AdminCount()
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return userManager.Users.Count();
    }
}
