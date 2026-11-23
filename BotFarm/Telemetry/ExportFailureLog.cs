using OpenTelemetry;

namespace BotFarm.Telemetry;

/// <summary>
/// Logs metric export outages once: one warning on the first failure, after startup or after a success, and one
/// information line on the first success after a failure. Messages name only the endpoint's host, never its path,
/// query or headers.
/// </summary>
/// <remarks>
/// Not thread-safe; the metric reader calls its exporter, and so <see cref="Report"/>, one export at a time.
/// </remarks>
internal sealed class ExportFailureLog
{
    private readonly string _host;
    private readonly ILogger _logger;
    private bool _failing;

    /// <param name="endpoint">Where metrics are exported to; only its host is logged.</param>
    /// <param name="logger">Receives the outage and recovery lines.</param>
    public ExportFailureLog(Uri endpoint, ILogger logger)
    {
        _host = endpoint.Host;
        _logger = logger;
    }

    /// <summary>
    /// Records the result of one export and logs the start or end of an outage.
    /// </summary>
    public void Report(ExportResult result)
    {
        if (result == ExportResult.Failure && !_failing)
        {
            _failing = true;
            _logger.LogWarning(
                "Metrics export to {OtlpHost} failed. Further failures are not logged until export recovers.",
                _host);
        }
        else if (result == ExportResult.Success && _failing)
        {
            _failing = false;
            _logger.LogInformation("Metrics export to {OtlpHost} recovered.", _host);
        }
    }
}
