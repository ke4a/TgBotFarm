using BotFarm.Authentication;
using BotFarm.Core.Models;
using BotFarm.Pages.Account;
using BotFarm.UnitTests.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.UnitTests.Pages.Account;

[TestFixture]
public class SetupModelTests
{
    private UserManager<ApplicationUser> _userManager;
    private const string SetupCode = "configured-setup-code-0123";

    private SignInManager<ApplicationUser> _signInManager;
    private SetupCodeService _setupCodeService = null!;
    private ILogger<SetupModel> _logger = null!;
    private SetupModel _pageModel;

    [SetUp]
    public void SetUp()
    {
        _userManager = IdentityTestFactory.CreateUserManager();
        _signInManager = IdentityTestFactory.CreateSignInManager(_userManager);

        _setupCodeService = SetupCodeServiceTests.CreateService(SetupCode);
        _logger = Substitute.For<ILogger<SetupModel>>();

        _pageModel = new SetupModel(_userManager, _signInManager, _setupCodeService, _logger)
        {
            SetupCode = SetupCode
        };

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

    [TestCase(null)]
    [TestCase("")]
    [TestCase("wrong-setup-code-0123")]
    public async Task OnPost_WrongOrMissingSetupCode_ReturnsGenericErrorWithoutCreatingUser(string? setupCode)
    {
        _pageModel.SetupCode = setupCode!;
        _pageModel.UserName = "admin";
        _pageModel.Password = "Password123!";
        _pageModel.ConfirmPassword = "Password123!";

        var result = await _pageModel.OnPost();

        Assert.That(result, Is.InstanceOf<PageResult>());
        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Invalid setup code."));
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        await _signInManager.DidNotReceive().SignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<bool>(), Arg.Any<string?>());

        var entries = _logger.LoggedEntries();
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Warning));
        if (!string.IsNullOrEmpty(setupCode))
        {
            Assert.That(entries[0].Message, Does.Not.Contain(setupCode));
        }
    }

    [Test]
    public async Task OnPost_WrongSetupCodeAndMissingCredentials_ReportsInvalidCodeFirst()
    {
        _pageModel.SetupCode = "wrong-setup-code-0123";

        await _pageModel.OnPost();

        Assert.That(_pageModel.ErrorMessage, Is.EqualTo("Invalid setup code."));
    }

    [Test]
    public async Task OnPost_ValidRequest_InvalidatesSetupCodeSoSecondAttemptFails()
    {
        _pageModel.UserName = "admin";
        _pageModel.Password = "Password123!";
        _pageModel.ConfirmPassword = "Password123!";
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), "Password123!").Returns(IdentityResult.Success);

        await _pageModel.OnPost();

        Assert.That(_setupCodeService.Verify(SetupCode), Is.False);

        // The users list is still mocked as empty, so only the erased code stops a second admin.
        var secondAttempt = new SetupModel(_userManager, _signInManager, _setupCodeService, _logger)
        {
            SetupCode = SetupCode,
            UserName = "intruder",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        var result = await secondAttempt.OnPost();

        Assert.That(result, Is.InstanceOf<PageResult>());
        Assert.That(secondAttempt.ErrorMessage, Is.EqualTo("Invalid setup code."));
        await _userManager.Received(1).CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Test]
    public async Task OnPost_UsersCreatedBeforeLockAcquired_RedirectsToLoginWithoutCreatingUser()
    {
        // First check (in the page) sees no users; the re-check inside the setup lock sees one.
        _userManager.Users.Returns(
            new List<ApplicationUser>().AsQueryable(),
            new List<ApplicationUser> { new("other") }.AsQueryable());
        _pageModel.UserName = "admin";
        _pageModel.Password = "Password123!";
        _pageModel.ConfirmPassword = "Password123!";

        var result = await _pageModel.OnPost() as RedirectToPageResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PageName, Is.EqualTo("/Account/Login"));
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        await _signInManager.DidNotReceive().SignInAsync(Arg.Any<ApplicationUser>(), Arg.Any<bool>(), Arg.Any<string?>());
    }
}
