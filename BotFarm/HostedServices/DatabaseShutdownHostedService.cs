using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BotFarm.HostedServices;

/// <summary>
/// Disconnects all database services during a graceful host shutdown.
/// </summary>
public class DatabaseShutdownHostedService : IHostedService
{
    private readonly IEnumerable<IDatabaseService> _databaseServices;
    private readonly ILogger<DatabaseShutdownHostedService> _logger;

    public DatabaseShutdownHostedService(
        IEnumerable<IDatabaseService> databaseServices,
        ILogger<DatabaseShutdownHostedService> logger)
    {
        _databaseServices = databaseServices;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("Hosting environment initiated shutdown.");

        foreach (var databaseService in _databaseServices)
        {
            await databaseService.Disconnect();
        }
    }
}
