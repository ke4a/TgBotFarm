using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.Health;

/// <summary>
/// Logs the result of each periodic health-check run.
/// </summary>
/// <remarks>
/// Registering a publisher makes ASP.NET Core run every health check on a timer, so checks with side effects,
/// such as the memory warning sent to Telegram, keep running when nobody opens the health page.
/// </remarks>
public sealed class HealthReportLogPublisher : IHealthCheckPublisher
{
    private readonly ILogger<HealthReportLogPublisher> _logger;

    /// <summary>
    /// Creates the publisher.
    /// </summary>
    public HealthReportLogPublisher(ILogger<HealthReportLogPublisher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs the overall status at Information level when healthy, and at Warning level with each failing check
    /// otherwise.
    /// </summary>
    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        if (report.Status == HealthStatus.Healthy)
        {
            _logger.LogInformation("Health checks: {Status} in {Duration} ms.",
                report.Status, report.TotalDuration.TotalMilliseconds);
            return Task.CompletedTask;
        }

        var failing = report.Entries
            .Where(entry => entry.Value.Status != HealthStatus.Healthy)
            .Select(entry => $"{entry.Key}={entry.Value.Status}");
        _logger.LogWarning(report.Entries.Values.FirstOrDefault(entry => entry.Exception is not null).Exception,
            "Health checks: {Status}. Failing: {FailingChecks}.", report.Status, string.Join(", ", failing));

        return Task.CompletedTask;
    }
}
