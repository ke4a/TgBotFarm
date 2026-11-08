using BotFarm.Authentication;
using BotFarm.Pages.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace BotFarm.UnitTests.Pages.Account;

[TestFixture]
public class LogoutModelTests
{
    private UserManager<ApplicationUser> _userManager;
    private SignInManager<ApplicationUser> _signInManager;

    [SetUp]
    public void SetUp()
    {
        _userManager = IdentityTestFactory.CreateUserManager();
        _signInManager = IdentityTestFactory.CreateSignInManager(_userManager);
    }

    [TearDown]
    public void TearDown()
    {
        _userManager.Dispose();
    }

    [Test]
    public void OnGet_RedirectsHomeWithoutSigningOut()
    {
        var pageModel = new LogoutModel(_signInManager);

        var result = pageModel.OnGet() as LocalRedirectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Url, Is.EqualTo("~/"));
        _signInManager.DidNotReceive().SignOutAsync();
    }

    [Test]
    public async Task OnPost_SignsOutAndRedirectsToLoginPage()
    {
        var pageModel = new LogoutModel(_signInManager);

        var result = await pageModel.OnPost() as RedirectToPageResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PageName, Is.EqualTo("/Account/Login"));
        await _signInManager.Received(1).SignOutAsync();
    }

    [Test]
    public void LogoutModel_DoesNotOptOutOfAntiforgeryValidation()
    {
        var ignoresAntiforgery = typeof(LogoutModel)
            .GetCustomAttributes(inherit: true)
            .OfType<IgnoreAntiforgeryTokenAttribute>()
            .Any();

        Assert.That(ignoresAntiforgery, Is.False);
    }
}
