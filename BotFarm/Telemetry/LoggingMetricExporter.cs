using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

namespace BotFarm.Telemetry;

/// <summary>
/// The OTLP metric exporter, with export outages logged once through <see cref="ExportFailureLog"/>: a warning on
/// the first failure and an information line on the recovery, naming only the endpoint's host.
/// </summary>
/// <remarks>
/// The OpenTelemetry SDK reports exporter failures only through its own event source, which the app's log does not
/// see, so each export result is passed to <see cref="ExportFailureLog"/>.
/// </remarks>
internal sealed class LoggingMetricExporter : OtlpMetricExporter
{
    private readonly ExportFailureLog _failureLog;

    /// <param name="options">The OTLP exporter options; their headers are never logged.</param>
    /// <param name="logger">Receives the outage and recovery lines.</param>
    public LoggingMetricExporter(OtlpExporterOptions options, ILogger logger)
        : base(options)
    {
        _failureLog = new ExportFailureLog(options.Endpoint, logger);
    }

    /// <inheritdoc />
    public override ExportResult Export(in Batch<Metric> metrics)
    {
        var result = base.Export(in metrics);
        _failureLog.Report(result);
        return result;
    }
}
