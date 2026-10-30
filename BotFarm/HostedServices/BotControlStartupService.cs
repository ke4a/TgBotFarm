using BotFarm.Core.Services;

namespace BotFarm.HostedServices;

/// <summary>
/// Loads each bot's saved enabled/disabled state and applies it once the application has started.
/// </summary>
public class BotControlStartupService : BackgroundService
{
    private readonly BotControlCoordinator _coordinator;
    private readonly IHostApplicationLifetime _appLifetime;

    /// <summary>
    /// Creates the startup service.
    /// </summary>
    public BotControlStartupService(BotControlCoordinator coordinator, IHostApplicationLifetime appLifetime)
    {
        _coordinator = coordinator;
        _appLifetime = appLifetime;
    }

    /// <summary>
    /// Waits for the application to finish starting, then initializes every bot.
    /// Stopping the application during initialization ends it quietly.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Bots register their webhooks here, so wait until the web server is accepting requests.
            await WaitForApplicationStartedAsync(stoppingToken);
            await _coordinator.InitializeAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (_appLifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        await using (stoppingToken.Register(() => started.TrySetCanceled(stoppingToken)))
        {
            await started.Task;
        }
    }
}
