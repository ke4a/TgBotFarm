using BotFarm.Core.Models;
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
    public async Task OnGet_SignsOutAndRedirectsToLoginPage()
    {
        var pageModel = new LogoutModel(_signInManager);

        var result = await pageModel.OnGet() as RedirectToPageResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PageName, Is.EqualTo("/Account/Login"));
        await _signInManager.Received(1).SignOutAsync();
    }
}
