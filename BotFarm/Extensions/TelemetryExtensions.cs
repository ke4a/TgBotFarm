using System.Reflection;
using BotFarm.Core.Telemetry;
using BotFarm.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace BotFarm.Extensions;

/// <summary>
/// Registers the export of BotFarm's metrics and logs over OTLP.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>Name of .NET's built-in meter for garbage collection, thread pool, CPU and process memory.</summary>
    internal const string RuntimeMeterName = "System.Runtime";

    /// <summary>The <c>service.name</c> resource attribute of the exported metrics and logs.</summary>
    internal const string ServiceName = "BotFarm";

    /// <summary>How often the collected metrics are exported.</summary>
    internal static readonly TimeSpan ExportInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Turns on the export of the <see cref="UpdateMetrics.MeterName"/> and <see cref="RuntimeMeterName"/> meters,
    /// and of the app's logs, when an endpoint is set in <c>Telemetry:OtlpEndpoint</c> or
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, with the app's setting taking precedence. When neither is set, nothing is
    /// registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With export on, metrics are sent over OTLP HTTP/protobuf once every <see cref="ExportInterval"/> and when the
    /// host stops, and logs are sent in batches as they are written, both with the resource <c>service.name</c>
    /// <see cref="ServiceName"/> and <c>service.version</c> the host's informational version.
    /// </para>
    /// <para>
    /// The endpoint is <c>Telemetry:OtlpEndpoint</c> plus <see cref="Core.Constants.Telemetry.MetricsPath"/> or
    /// <see cref="Core.Constants.Telemetry.LogsPath"/> when set, otherwise the exporter's own handling of
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>. The headers are <c>Telemetry:OtlpHeaders</c> when set, otherwise
    /// <c>OTEL_EXPORTER_OTLP_HEADERS</c>. Metric export failures are logged once per outage, naming only the
    /// endpoint's host; headers are never logged. Log export failures are not logged, since those lines would go to
    /// the failing export too; a metric export outage reports the same endpoint being down.
    /// </para>
    /// <para>
    /// Exported logs follow the <c>Logging</c> configuration like the other log providers, under the provider alias
    /// <c>OpenTelemetry</c>, so <c>Logging:OpenTelemetry:LogLevel</c> sets levels for the export alone. Each record
    /// carries its formatted message and its structured values.
    /// </para>
    /// <para>
    /// At startup, <see cref="TelemetryStartupService"/> creates the <see cref="AppMemoryMetrics"/> gauges and starts
    /// every registered bot's counts at zero.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddBotFarmTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = OtlpExportSettings.Resolve(configuration);
        if (settings is null)
        {
            return services;
        }

        services.AddSingleton(settings);
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: ServiceName,
                serviceVersion: GetInformationalVersion()))
            .WithMetrics(metrics => metrics
                .AddMeter(UpdateMetrics.MeterName, RuntimeMeterName)
                .AddReader(CreateReader))
            .WithLogging(
                logging => logging.AddProcessor(CreateLogProcessor),
                options => options.IncludeFormattedMessage = true);

        services.AddSingleton<AppMemoryMetrics>();

        // After AddOpenTelemetry, so the meter provider is listening when the zeros are recorded.
        services.AddHostedService<TelemetryStartupService>();

        return services;
    }

    private static MetricReader CreateReader(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<OtlpExportSettings>()
            .CreateExporterOptions(Core.Constants.Telemetry.MetricsPath);
        var exporter = new LoggingMetricExporter(
            options,
            serviceProvider.GetRequiredService<ILogger<LoggingMetricExporter>>());

        return new PeriodicExportingMetricReader(exporter, (int)ExportInterval.TotalMilliseconds);
    }

    private static BaseProcessor<LogRecord> CreateLogProcessor(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<OtlpExportSettings>()
            .CreateExporterOptions(Core.Constants.Telemetry.LogsPath);

        return new BatchLogRecordExportProcessor(new OtlpLogExporter(options));
    }

    private static string? GetInformationalVersion()
    {
        return typeof(TelemetryExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
    }
}
