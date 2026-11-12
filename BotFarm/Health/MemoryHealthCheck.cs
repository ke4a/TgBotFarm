using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BotFarm.Health;

/// <summary>
/// Monitors the process's working set (the physical memory it uses) and emits Telegram warnings when the
/// configured threshold is exceeded.
/// </summary>
public class MemoryHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<MemoryCheckOptions> _options;
    private readonly ILogger<MemoryHealthCheck> _logger;
    private readonly INotificationService _notificationService;
    private readonly string _botName;

    private const string logPrefix = $"[{nameof(MemoryHealthCheck)}]";

    /// <summary>
    /// Creates the memory health check and chooses a bot that can receive warning notifications.
    /// </summary>
    public MemoryHealthCheck(
        IOptionsMonitor<MemoryCheckOptions> options,
        IEnumerable<IBotModule> botModules,
        ILogger<MemoryHealthCheck> logger,
        INotificationService notificationService)
    {
        _options = options;
        _logger = logger;
        _notificationService = notificationService;
        _botName = botModules.First().Name; // send to any bot
    }

    /// <summary>
    /// Reports the working set and the GC heap size, and reports unhealthy once the working set reaches the
    /// configured threshold.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Get(context.Registration.Name);

        var workingSet = Environment.WorkingSet;
        var data = new Dictionary<string, object>()
        {
            { "WorkingSetBytes", workingSet },
            // Measured at the last garbage collection, so it leaves out uncollected garbage and shows steady growth.
            { "GCHeapSizeBytes", GC.GetGCMemoryInfo().HeapSizeBytes },
            { "Gen0Collections", GC.CollectionCount(0) },
            { "Gen1Collections", GC.CollectionCount(1) },
            { "Gen2Collections", GC.CollectionCount(2) },
        };
        var status = (workingSet < options.Threshold) ? HealthStatus.Healthy : HealthStatus.Unhealthy;

        if (status == HealthStatus.Healthy)
        {
            _logger.LogInformation("{LogPrefix} Current memory usage: {MemoryMb} MB.", logPrefix, workingSet / 1024 / 1024);
        }
        else
        {
            var message = $"{logPrefix} Memory usage ({workingSet / 1024 / 1024} MB) exceeded threshold ({options.Threshold / 1024 / 1024} MB).";
            _logger.LogWarning(
                "{LogPrefix} Memory usage ({MemoryMb} MB) exceeded threshold ({ThresholdMb} MB).",
                logPrefix,
                workingSet / 1024 / 1024,
                options.Threshold / 1024 / 1024);
            await _notificationService.SendWarningNotification(message, _botName);
        }

        return new HealthCheckResult(
            status,
            description: $"Reports unhealthy status if the working set is at least {options.Threshold / 1024 / 1024} MB.",
            exception: null,
            data: data);
    }
}

/// <summary>
/// Configuration for <see cref="MemoryHealthCheck"/>.
/// </summary>
public class MemoryCheckOptions
{
    // Working set at which the check fails (in bytes)
    public long Threshold { get; set; } = 1024L * 1024L * 400; // ~400 MB
}
