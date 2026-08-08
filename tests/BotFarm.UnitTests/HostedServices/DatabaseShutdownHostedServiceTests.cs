using BotFarm.Core.Abstractions;
using BotFarm.HostedServices;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.UnitTests.HostedServices;

[TestFixture]
public class DatabaseShutdownHostedServiceTests
{
    private ILogger<DatabaseShutdownHostedService> _logger;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<DatabaseShutdownHostedService>>();
    }

    [Test]
    public async Task StartAsync_WhenCalled_DoesNotDisconnectDatabaseServices()
    {
        var databaseService = Substitute.For<IDatabaseService>();
        var service = new DatabaseShutdownHostedService([databaseService], _logger);

        await service.StartAsync(CancellationToken.None);

        await databaseService.DidNotReceive().Disconnect();
    }

    [Test]
    public async Task StopAsync_WhenNoDatabaseServices_LogsWarning()
    {
        var service = new DatabaseShutdownHostedService([], _logger);

        await service.StopAsync(CancellationToken.None);

        _logger.Received(1).LogWarning("Hosting environment initiated shutdown.");
    }

    [Test]
    public async Task StopAsync_WhenSingleDatabaseServiceExists_DisconnectsIt()
    {
        var databaseService = Substitute.For<IDatabaseService>();
        var service = new DatabaseShutdownHostedService([databaseService], _logger);

        await service.StopAsync(CancellationToken.None);

        _logger.Received(1).LogWarning("Hosting environment initiated shutdown.");
        await databaseService.Received(1).Disconnect();
    }

    [Test]
    public async Task StopAsync_WhenMultipleDatabaseServicesExist_DisconnectsAll()
    {
        var firstService = Substitute.For<IDatabaseService>();
        var secondService = Substitute.For<IDatabaseService>();
        var thirdService = Substitute.For<IDatabaseService>();
        var service = new DatabaseShutdownHostedService([firstService, secondService, thirdService], _logger);

        await service.StopAsync(CancellationToken.None);

        _logger.Received(1).LogWarning("Hosting environment initiated shutdown.");
        await firstService.Received(1).Disconnect();
        await secondService.Received(1).Disconnect();
        await thirdService.Received(1).Disconnect();
    }
}
