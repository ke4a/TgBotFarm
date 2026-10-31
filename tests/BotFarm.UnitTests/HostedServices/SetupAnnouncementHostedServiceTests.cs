using BotFarm.Authentication;
using BotFarm.HostedServices;
using BotFarm.UnitTests.Authentication;
using BotFarm.UnitTests.Pages.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BotFarm.UnitTests.HostedServices;

[TestFixture]
public class SetupAnnouncementHostedServiceTests
{
    private const string ConfiguredCode = "configured-setup-code-0123";

    private UserManager<ApplicationUser> _userManager = null!;
    private ServiceProvider _serviceProvider = null!;
    private ILogger<SetupAnnouncementHostedService> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _userManager = IdentityTestFactory.CreateUserManager();
        _userManager.Users.Returns(new List<ApplicationUser>().AsQueryable());
        _serviceProvider = new ServiceCollection()
            .AddScoped(_ => _userManager)
            .BuildServiceProvider();
        _logger = Substitute.For<ILogger<SetupAnnouncementHostedService>>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        _userManager.Dispose();
    }

    private async Task RunAsync(SetupCodeService setupCodeService)
    {
        using var service = new SetupAnnouncementHostedService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(), setupCodeService, _logger);
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;
        await service.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task ExecuteAsync_NoUsersAndGeneratedCode_LogsCodeAsWarning()
    {
        var setupCodeService = SetupCodeServiceTests.CreateService();

        await RunAsync(setupCodeService);

        var entries = _logger.LoggedEntries();
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entries[0].Message, Is.EqualTo(
            $"No admin account exists. Open /Account/Setup and enter setup code: {setupCodeService.GeneratedCode}"));
    }

    [Test]
    public async Task ExecuteAsync_NoUsersAndConfiguredCode_LogsPendingWithoutValue()
    {
        await RunAsync(SetupCodeServiceTests.CreateService(ConfiguredCode));

        var entries = _logger.LoggedEntries();
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entries[0].Message, Does.Contain("Setup:Code"));
        Assert.That(entries[0].Message, Does.Not.Contain(ConfiguredCode));
    }

    [Test]
    public async Task ExecuteAsync_UsersExist_LogsNothing()
    {
        _userManager.Users.Returns(new List<ApplicationUser> { new("admin") }.AsQueryable());

        await RunAsync(SetupCodeServiceTests.CreateService());

        Assert.That(_logger.LoggedEntries(), Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_IdentityStoreUnreachable_LogsWarningWithoutCodeAndDoesNotThrow()
    {
        _userManager.Users.Throws(new TimeoutException("Mongo unreachable"));
        var setupCodeService = SetupCodeServiceTests.CreateService();

        await RunAsync(setupCodeService);

        var entries = _logger.LoggedEntries();
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entries[0].Exception, Is.InstanceOf<TimeoutException>());
        Assert.That(entries[0].Message, Does.Not.Contain(setupCodeService.GeneratedCode!));
    }
}
