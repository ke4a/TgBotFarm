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

    /// <summary>
    /// Creates the shutdown hook that disconnects every registered <see cref="IDatabaseService"/>.
    /// </summary>
    public DatabaseShutdownHostedService(
        IEnumerable<IDatabaseService> databaseServices,
        ILogger<DatabaseShutdownHostedService> logger)
    {
        _databaseServices = databaseServices;
        _logger = logger;
    }

    /// <summary>
    /// No startup work is required; this service only participates in shutdown.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Disconnects all database services.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("Hosting environment initiated shutdown.");

        foreach (var databaseService in _databaseServices)
        {
            await databaseService.Disconnect();
        }
    }
}
