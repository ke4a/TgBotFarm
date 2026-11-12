using BotFarm.Authentication;
using BotFarm.Pages.Account;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NSubstitute;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace BotFarm.UnitTests.Pages.Account;

[TestFixture]
public class LoginModelTests : AdminUiTestFixture
{
    private UserManager<ApplicationUser> _userManager;
    private SignInManager<ApplicationUser> _signInManager;
    private LoginModel _pageModel;

    [SetUp]
    public void SetUp()
    {
        _userManager = IdentityTestFactory.CreateUserManager();
        _signInManager = IdentityTestFactory.CreateSignInManager(_userManager);

        _pageModel = new LoginModel(_signInManager, _userManager, AdminLocalizationTestExtensions.CreateAdminLocalizer<LoginModel>());
    }

    [TearDown]
    public void TearDown()
    {
        _userManager.Dispose();
    }

    [Test]
    public async Task OnGet_NoUsersExist_RedirectsToSetupPage()
    {
        _userManager.Users.Returns(new List<ApplicationUser>().AsQueryable());

        var result = await _pageModel.OnGet() as RedirectToPageResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PageName, Is.EqualTo("/Account/Setup"));
    }

    [Test]
    public async Task OnGet_UsersExist_ReturnsPage()
    {
        _userManager.Users.Returns(new List<ApplicationUser> { new("admin") }.AsQueryable());

        var result = await _pageModel.OnGet();

        Assert.That(result, Is.InstanceOf<PageResult>());
    }

    [TestCase("", "password")]
    [TestCase("admin", "")]
    [TestCase(null, null)]
    public async Task OnPost_MissingCredentials_ReturnsPageWithError(string? userName, string? password)
    {
        _pageModel.UserName = userName ?? string.Empty;
        _pageModel.Password = password ?? string.Empty;

        var result = await _pageModel.OnPost();

        Assert.That(result, Is.InstanceOf<PageResult>());
        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Username and password are required."));
        await _signInManager.DidNotReceive().PasswordSignInAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Test]
    public async Task OnPost_ValidCredentials_RedirectsToReturnUrl()
    {
        _pageModel.UserName = "admin";
        _pageModel.Password = "correct-password";
        _pageModel.ReturnUrl = "/dashboard";

        _signInManager.PasswordSignInAsync("admin", "correct-password", true, true)
            .Returns(SignInResult.Success);

        var result = await _pageModel.OnPost() as LocalRedirectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Url, Is.EqualTo("/dashboard"));
    }

    [Test]
    public async Task OnPost_ValidCredentialsNoReturnUrl_RedirectsToRoot()
    {
        _pageModel.UserName = "admin";
        _pageModel.Password = "correct-password";

        _signInManager.PasswordSignInAsync("admin", "correct-password", true, true)
            .Returns(SignInResult.Success);

        var result = await _pageModel.OnPost() as LocalRedirectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Url, Is.EqualTo("~/"));
    }

    [Test]
    public async Task OnPost_LockedOut_ReturnsPageWithLockoutMessage()
    {
        _pageModel.UserName = "admin";
        _pageModel.Password = "wrong-password";

        _signInManager.PasswordSignInAsync("admin", "wrong-password", true, true)
            .Returns(SignInResult.LockedOut);

        var result = await _pageModel.OnPost();

        Assert.That(result, Is.InstanceOf<PageResult>());
        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Account locked due to too many failed attempts. Try again later."));
    }

    [Test]
    public async Task OnPost_InvalidCredentials_ReturnsPageWithGenericError()
    {
        _pageModel.UserName = "admin";
        _pageModel.Password = "wrong-password";

        _signInManager.PasswordSignInAsync("admin", "wrong-password", true, true)
            .Returns(SignInResult.Failed);

        var result = await _pageModel.OnPost();

        Assert.That(result, Is.InstanceOf<PageResult>());
        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Invalid username or password."));
    }

    [Test]
    public async Task OnPost_MissingCredentialsInRussian_ReturnsRussianError()
    {
        using var _ = new CultureScope("ru-RU");

        await _pageModel.OnPost();

        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Укажите имя пользователя и пароль."));
    }

    [Test]
    public async Task OnPost_LockedOutInRussian_ReturnsRussianLockoutMessage()
    {
        using var _ = new CultureScope("ru-RU");
        _pageModel.UserName = "admin";
        _pageModel.Password = "wrong-password";
        _signInManager.PasswordSignInAsync("admin", "wrong-password", true, true)
            .Returns(SignInResult.LockedOut);

        await _pageModel.OnPost();

        Assert.That(_pageModel.ErrorMessage, Is.EqualTo(
            "Учётная запись заблокирована из-за слишком большого числа неудачных попыток. Повторите попытку позже."));
    }

    [Test]
    public async Task OnPost_InvalidCredentialsInRussian_ReturnsRussianGenericError()
    {
        using var _ = new CultureScope("ru-RU");
        _pageModel.UserName = "admin";
        _pageModel.Password = "wrong-password";
        _signInManager.PasswordSignInAsync("admin", "wrong-password", true, true)
            .Returns(SignInResult.Failed);

        await _pageModel.OnPost();

        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Неверное имя пользователя или пароль."));
    }
}
