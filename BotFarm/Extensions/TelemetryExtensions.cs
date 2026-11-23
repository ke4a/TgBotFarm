using System.Reflection;
using BotFarm.Core.Telemetry;
using BotFarm.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace BotFarm.Extensions;

/// <summary>
/// Registers the export of BotFarm's metrics over OTLP.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>Name of .NET's built-in meter for garbage collection, thread pool, CPU and process memory.</summary>
    internal const string RuntimeMeterName = "System.Runtime";

    /// <summary>The <c>service.name</c> resource attribute of the exported metrics.</summary>
    internal const string ServiceName = "BotFarm";

    /// <summary>How often the collected metrics are exported.</summary>
    internal static readonly TimeSpan ExportInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Turns on the export of the <see cref="UpdateMetrics.MeterName"/> and <see cref="RuntimeMeterName"/> meters
    /// when an endpoint is set in <c>Telemetry:OtlpEndpoint</c> or <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, with the
    /// app's setting taking precedence. When neither is set, nothing is registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With export on, metrics are sent over OTLP HTTP/protobuf once every <see cref="ExportInterval"/> and when the
    /// host stops, with the resource <c>service.name</c> <see cref="ServiceName"/> and <c>service.version</c> the
    /// host's informational version.
    /// </para>
    /// <para>
    /// The endpoint is <c>Telemetry:OtlpEndpoint</c> plus <see cref="Core.Constants.Telemetry.MetricsPath"/> when
    /// set, otherwise the exporter's own handling of <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>. The headers are
    /// <c>Telemetry:OtlpHeaders</c> when set, otherwise <c>OTEL_EXPORTER_OTLP_HEADERS</c>. Export failures are
    /// logged once per outage, naming only the endpoint's host; headers are never logged.
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
                .AddReader(CreateReader));

        services.AddSingleton<AppMemoryMetrics>();

        // After AddOpenTelemetry, so the meter provider is listening when the zeros are recorded.
        services.AddHostedService<TelemetryStartupService>();

        return services;
    }

    private static MetricReader CreateReader(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<OtlpExportSettings>().CreateExporterOptions();
        var exporter = new LoggingMetricExporter(
            options,
            serviceProvider.GetRequiredService<ILogger<LoggingMetricExporter>>());

        return new PeriodicExportingMetricReader(exporter, (int)ExportInterval.TotalMilliseconds);
    }

    private static string? GetInformationalVersion()
    {
        return typeof(TelemetryExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
    }
}
