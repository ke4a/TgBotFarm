namespace BotFarm.HostedServices;

/// <summary>
/// Stops the application after the number of hours set in <c>ScheduledJobs:ShutdownEveryHours</c>,
/// so an external supervisor can restart it. Does nothing when the setting is zero or less.
/// </summary>
public class PeriodicShutdownService : BackgroundService
{
    internal const string ShutdownEveryHoursKey = "ScheduledJobs:ShutdownEveryHours";

    private readonly IHostApplicationLifetime _appLifetime;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PeriodicShutdownService> _logger;
    private readonly int _shutdownEveryHours;

    /// <summary>
    /// Creates the shutdown service and reads the shutdown interval from configuration.
    /// </summary>
    public PeriodicShutdownService(
        IHostApplicationLifetime appLifetime,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<PeriodicShutdownService> logger)
    {
        _appLifetime = appLifetime;
        _timeProvider = timeProvider;
        _logger = logger;
        _shutdownEveryHours = configuration.GetValue<int>(ShutdownEveryHoursKey);
    }

    /// <summary>
    /// Waits for the configured interval and then asks the application to stop.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_shutdownEveryHours <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(_shutdownEveryHours), _timeProvider);
        try
        {
            if (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _logger.LogWarning("Scheduled shutdown.");
                _appLifetime.StopApplication();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
