using BotFarm.Core.Models;
using BotFarm.Pages.Account;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NSubstitute;

namespace BotFarm.UnitTests.Pages.Account;

[TestFixture]
public class SetupModelTests
{
    private UserManager<ApplicationUser> _userManager;
    private SignInManager<ApplicationUser> _signInManager;
    private SetupModel _pageModel;

    [SetUp]
    public void SetUp()
    {
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);

        _signInManager = Substitute.For<SignInManager<ApplicationUser>>(
            _userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null, null, null, null);

        _pageModel = new SetupModel(_userManager, _signInManager);

        _userManager.Users.Returns(new List<ApplicationUser>().AsQueryable());
    }

    [TearDown]
    public void TearDown()
    {
        _userManager.Dispose();
    }

    [Test]
    public void OnGet_NoUsersExist_ReturnsPage()
    {
        var result = _pageModel.OnGet();

        Assert.That(result, Is.InstanceOf<PageResult>());
    }

    [Test]
    public void OnGet_UsersAlreadyExist_RedirectsToLoginPage()
    {
        _userManager.Users.Returns(new List<ApplicationUser> { new("admin") }.AsQueryable());

        var result = _pageModel.OnGet() as RedirectToPageResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PageName, Is.EqualTo("/Account/Login"));
    }

    [Test]
    public async Task OnPost_UsersAlreadyExist_RedirectsToLoginPageWithoutCreatingUser()
    {
        _userManager.Users.Returns(new List<ApplicationUser> { new("admin") }.AsQueryable());
        _pageModel.UserName = "newadmin";
        _pageModel.Password = "Password123!";
        _pageModel.ConfirmPassword = "Password123!";

        var result = await _pageModel.OnPost() as RedirectToPageResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PageName, Is.EqualTo("/Account/Login"));
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        await _signInManager.DidNotReceive().SignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<bool>(), Arg.Any<string?>());
    }

    [TestCase("", "Password123!", "Password123!")]
    [TestCase("admin", "", "")]
    public async Task OnPost_MissingCredentials_ReturnsPageWithError(string userName, string password, string confirmPassword)
    {
        _pageModel.UserName = userName;
        _pageModel.Password = password;
        _pageModel.ConfirmPassword = confirmPassword;

        var result = await _pageModel.OnPost();

        Assert.That(result, Is.InstanceOf<PageResult>());
        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Username and password are required."));
    }

    [Test]
    public async Task OnPost_PasswordsDoNotMatch_ReturnsPageWithError()
    {
        _pageModel.UserName = "admin";
        _pageModel.Password = "Password123!";
        _pageModel.ConfirmPassword = "DifferentPassword123!";

        var result = await _pageModel.OnPost();

        Assert.That(result, Is.InstanceOf<PageResult>());
        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Passwords do not match."));
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        await _signInManager.DidNotReceive().SignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<bool>(), Arg.Any<string?>());
    }

    [Test]
    public async Task OnPost_UserCreationFails_ReturnsPageWithErrorsJoined()
    {
        _pageModel.UserName = "admin";
        _pageModel.Password = "weak";
        _pageModel.ConfirmPassword = "weak";

        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), "weak").Returns(IdentityResult.Failed(
            new IdentityError { Description = "Password too short." },
            new IdentityError { Description = "Password requires a digit." }));

        var result = await _pageModel.OnPost();

        Assert.That(result, Is.InstanceOf<PageResult>());
        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Password too short. Password requires a digit."));
        await _signInManager.DidNotReceive().SignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<bool>(), Arg.Any<string?>());
    }

    [Test]
    public async Task OnPost_ValidRequest_CreatesUserSignsInAndRedirectsToRoot()
    {
        _pageModel.UserName = "admin";
        _pageModel.Password = "Password123!";
        _pageModel.ConfirmPassword = "Password123!";

        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), "Password123!").Returns(IdentityResult.Success);

        var result = await _pageModel.OnPost() as LocalRedirectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Url, Is.EqualTo("~/"));
        await _userManager.Received(1).CreateAsync(
            Arg.Is<ApplicationUser>(u => u.UserName == "admin"), "Password123!");
        await _signInManager.Received(1).SignInAsync(
            Arg.Is<ApplicationUser>(u => u.UserName == "admin"), true, null);
    }
}
