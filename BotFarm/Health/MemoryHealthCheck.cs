using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace BotFarm.Health;

public class MemoryHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<MemoryCheckOptions> _options;
    private readonly ILogger<MemoryHealthCheck> _logger;
    private readonly INotificationService _notificationService;
    private readonly string _botName;

    private const string logPrefix = $"[{nameof(MemoryHealthCheck)}]";

    public MemoryHealthCheck(
        IOptionsMonitor<MemoryCheckOptions> options,
        IEnumerable<BotRegistration> registrations,
        ILogger<MemoryHealthCheck> logger,
        INotificationService notificationService)
    {
        _options = options;
        _logger = logger;
        _notificationService = notificationService;
        _botName = registrations.First().BotName; // send to any bot
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Get(context.Registration.Name);

        var allocated = Process.GetCurrentProcess().WorkingSet64;
        var gcTotal = GC.GetTotalMemory(forceFullCollection: false);
        var data = new Dictionary<string, object>()
        {
            { "AllocatedBytes", allocated },
            { "GCTotal", gcTotal },
            { "Gen0Collections", GC.CollectionCount(0) },
            { "Gen1Collections", GC.CollectionCount(1) },
            { "Gen2Collections", GC.CollectionCount(2) },
        };
        var status = (allocated < options.Threshold) ? HealthStatus.Healthy : HealthStatus.Unhealthy;

        if (status == HealthStatus.Healthy)
        {
            _logger.LogInformation($"{logPrefix} Current memory usage: {allocated / 1024 / 1024} MB.");
        }
        else 
        {
            var message = $"{logPrefix} Memory usage ({allocated / 1024 / 1024} MB) exceeded threshold ({options.Threshold / 1024 / 1024} MB).";
            _logger.LogWarning(message);
            await _notificationService.SendWarningNotification(message, _botName);
        }

        return new HealthCheckResult(
            status,
            description: $"Reports degraded status if allocated memory >= {options.Threshold / 1024 / 1024} MB.",
            exception: null,
            data: data);
    }
}
public class MemoryCheckOptions
{
    // Failure threshold (in bytes)
    public long Threshold { get; set; } = 1024L * 1024L * 400; // ~400 MB
}
