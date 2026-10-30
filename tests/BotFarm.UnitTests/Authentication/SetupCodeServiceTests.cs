using BotFarm.Authentication;
using BotFarm.Core.Models;
using BotFarm.UnitTests.Pages.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace BotFarm.UnitTests.Authentication;

[TestFixture]
public class SetupCodeServiceTests
{
    private const string ConfiguredCode = "configured-setup-code-0123";

    private UserManager<ApplicationUser> _userManager = null!;
    private List<ApplicationUser> _users = null!;

    [SetUp]
    public void SetUp()
    {
        _users = [];
        _userManager = IdentityTestFactory.CreateUserManager();
        _userManager.Users.Returns(_ => _users.ToList().AsQueryable());
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>()).Returns(async call =>
        {
            // Yield so concurrent callers genuinely overlap if they are not serialized.
            await Task.Delay(20);
            lock (_users)
            {
                _users.Add(call.Arg<ApplicationUser>());
            }

            return IdentityResult.Success;
        });
    }

    [TearDown]
    public void TearDown()
    {
        _userManager.Dispose();
    }

    internal static SetupCodeService CreateService(string? configuredCode = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SetupCodeService.ConfigurationKey] = configuredCode })
            .Build();
        return new SetupCodeService(configuration);
    }

    [Test]
    public void Constructor_NoConfiguredCode_GeneratesUnpaddedBase64UrlCodeOf16Bytes()
    {
        var service = CreateService();

        Assert.That(service.IsConfigured, Is.False);
        Assert.That(service.IsPending, Is.True);
        Assert.That(service.GeneratedCode, Has.Length.EqualTo(22));
        Assert.That(service.GeneratedCode, Does.Match("^[A-Za-z0-9_-]{22}$"));
        Assert.That(System.Buffers.Text.Base64Url.DecodeFromChars(service.GeneratedCode), Has.Length.EqualTo(16));
    }

    [Test]
    public void Constructor_NoConfiguredCode_GeneratesDifferentCodePerInstance()
    {
        Assert.That(CreateService().GeneratedCode, Is.Not.EqualTo(CreateService().GeneratedCode));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Constructor_WhitespaceConfiguredCode_GeneratesCode(string configuredCode)
    {
        var service = CreateService(configuredCode);

        Assert.That(service.IsConfigured, Is.False);
        Assert.That(service.GeneratedCode, Is.Not.Null);
    }

    [Test]
    public void Constructor_ConfiguredCode_UsesItWithoutExposingIt()
    {
        var service = CreateService(ConfiguredCode);

        Assert.That(service.IsConfigured, Is.True);
        Assert.That(service.IsPending, Is.True);
        Assert.That(service.GeneratedCode, Is.Null);
        Assert.That(service.Verify(ConfiguredCode), Is.True);
    }

    [Test]
    public void Constructor_ConfiguredCodeTooShort_ThrowsNamingSettingButNotValue()
    {
        const string shortCode = "short-secret-15";

        var ex = Assert.Throws<InvalidOperationException>(() => CreateService(shortCode));

        Assert.That(ex!.Message, Does.Contain("Setup:Code"));
        Assert.That(ex.Message, Does.Contain("16"));
        Assert.That(ex.Message, Does.Not.Contain(shortCode));
    }

    [Test]
    public void Verify_CorrectCode_ReturnsTrue()
    {
        var service = CreateService();

        Assert.That(service.Verify(service.GeneratedCode), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("wrong-code")]
    public void Verify_WrongOrMissingCode_ReturnsFalse(string? candidate)
    {
        var service = CreateService(ConfiguredCode);

        Assert.That(service.Verify(candidate), Is.False);
    }

    [Test]
    public void Verify_CodeWithDifferentCase_ReturnsFalse()
    {
        var service = CreateService(ConfiguredCode);

        Assert.That(service.Verify(ConfiguredCode.ToUpperInvariant()), Is.False);
    }

    [Test]
    public async Task CompleteSetupAsync_CorrectCode_CreatesUserAndInvalidatesCode()
    {
        var service = CreateService(ConfiguredCode);

        var result = await service.CompleteSetupAsync(_userManager, ConfiguredCode, "admin", "Password123!");

        Assert.That(result.Status, Is.EqualTo(SetupCompletionStatus.Succeeded));
        Assert.That(result.User!.UserName, Is.EqualTo("admin"));
        Assert.That(service.IsPending, Is.False);
        Assert.That(service.Verify(ConfiguredCode), Is.False);
    }

    [Test]
    public async Task CompleteSetupAsync_WithGeneratedCode_StopsExposingItAfterSuccess()
    {
        var service = CreateService();
        var code = service.GeneratedCode;

        var result = await service.CompleteSetupAsync(_userManager, code, "admin", "Password123!");

        Assert.That(result.Status, Is.EqualTo(SetupCompletionStatus.Succeeded));
        Assert.That(service.GeneratedCode, Is.Null);
        Assert.That(service.Verify(code), Is.False);
    }

    [Test]
    public async Task CompleteSetupAsync_WrongCode_DoesNotCreateUser()
    {
        var service = CreateService(ConfiguredCode);

        var result = await service.CompleteSetupAsync(_userManager, "wrong-code", "admin", "Password123!");

        Assert.That(result.Status, Is.EqualTo(SetupCompletionStatus.InvalidCode));
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        Assert.That(service.IsPending, Is.True);
    }

    [Test]
    public async Task CompleteSetupAsync_UsersAlreadyExist_DoesNotCreateUser()
    {
        _users.Add(new ApplicationUser("existing"));
        var service = CreateService(ConfiguredCode);

        var result = await service.CompleteSetupAsync(_userManager, ConfiguredCode, "admin", "Password123!");

        Assert.That(result.Status, Is.EqualTo(SetupCompletionStatus.AlreadyCompleted));
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Test]
    public async Task CompleteSetupAsync_IdentityRejectsUser_ReturnsErrorsAndKeepsCode()
    {
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), "weak")
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Password too short." }));
        var service = CreateService(ConfiguredCode);

        var result = await service.CompleteSetupAsync(_userManager, ConfiguredCode, "admin", "weak");

        Assert.That(result.Status, Is.EqualTo(SetupCompletionStatus.Failed));
        Assert.That(result.Errors!.Select(e => e.Description), Is.EqualTo(new[] { "Password too short." }));
        Assert.That(service.Verify(ConfiguredCode), Is.True);
    }

    [Test]
    public async Task CompleteSetupAsync_ConcurrentAttempts_CreatesOnlyOneUser()
    {
        var service = CreateService(ConfiguredCode);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Task.Run(() => service.CompleteSetupAsync(_userManager, ConfiguredCode, $"admin{i}", "Password123!"))));

        Assert.That(results.Count(r => r.Status == SetupCompletionStatus.Succeeded), Is.EqualTo(1));
        Assert.That(results.Count(r => r.Status == SetupCompletionStatus.AlreadyCompleted), Is.EqualTo(9));
        Assert.That(_users, Has.Count.EqualTo(1));
        await _userManager.Received(1).CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }
}
