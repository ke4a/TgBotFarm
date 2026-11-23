using BotFarm.Core;
using BotFarm.Core.Telemetry;
using OpenTelemetry.Exporter;

namespace BotFarm.Telemetry;

/// <summary>
/// The OTLP export settings resolved from configuration when export is on.
/// </summary>
/// <param name="BaseEndpoint">
/// The OTLP/HTTP base URL from <c>Telemetry:OtlpEndpoint</c>, without a trailing slash, or null to leave the endpoint
/// to the exporter's own handling of <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.
/// </param>
/// <param name="Headers">
/// The headers from <c>Telemetry:OtlpHeaders</c>, or null to leave them to <c>OTEL_EXPORTER_OTLP_HEADERS</c>.
/// </param>
internal sealed record OtlpExportSettings(Uri? BaseEndpoint, string? Headers)
{
    /// <summary>
    /// How long an idle connection to the OTLP endpoint is kept for reuse. Shorter than the export interval, so each
    /// export opens a fresh connection instead of reusing one the receiver or a proxy may just have closed.
    /// </summary>
    internal static readonly TimeSpan ConnectionIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The standard OpenTelemetry variable that names the OTLP endpoint.</summary>
    public const string StandardEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    private static readonly string EndpointKey =
        $"{Constants.Telemetry.SectionName}:{nameof(TelemetryOptions.OtlpEndpoint)}";

    private static readonly string HeadersKey =
        $"{Constants.Telemetry.SectionName}:{nameof(TelemetryOptions.OtlpHeaders)}";

    /// <summary>
    /// Returns the export settings, or null when export is off because neither <c>Telemetry:OtlpEndpoint</c> nor
    /// <see cref="StandardEndpointKey"/> is set in <paramref name="configuration"/>.
    /// </summary>
    /// <remarks>
    /// An endpoint that is not an absolute URL is left unset here; <see cref="TelemetryOptions"/> validation stops
    /// startup with a message naming the setting.
    /// </remarks>
    public static OtlpExportSettings? Resolve(IConfiguration configuration)
    {
        var appEndpoint = configuration[EndpointKey];
        var standardEndpoint = configuration[StandardEndpointKey];
        if (string.IsNullOrWhiteSpace(appEndpoint) && string.IsNullOrWhiteSpace(standardEndpoint))
        {
            return null;
        }

        Uri? baseEndpoint = null;
        if (!string.IsNullOrWhiteSpace(appEndpoint)
            && Uri.TryCreate(appEndpoint.Trim().TrimEnd('/'), UriKind.Absolute, out var parsedEndpoint))
        {
            baseEndpoint = parsedEndpoint;
        }

        var headers = configuration[HeadersKey];
        return new OtlpExportSettings(baseEndpoint, string.IsNullOrWhiteSpace(headers) ? null : headers);
    }

    /// <summary>
    /// Creates the options of an OTLP exporter: HTTP/protobuf, sending to <see cref="BaseEndpoint"/> plus
    /// <paramref name="signalPath"/> and with <see cref="Headers"/> when they are set, and otherwise with the values
    /// the exporter reads from the standard variables. Its HTTP client keeps idle connections for
    /// <see cref="ConnectionIdleTimeout"/> only.
    /// </summary>
    /// <param name="signalPath">
    /// The receiver path of the exported signal, <see cref="Constants.Telemetry.MetricsPath"/> or
    /// <see cref="Constants.Telemetry.LogsPath"/>.
    /// </param>
    /// <remarks>
    /// The default client keeps idle connections for a minute, the same as the export interval and the idle limit of
    /// common receivers, so an export now and then goes out on a connection the other side has just closed and fails.
    /// </remarks>
    public OtlpExporterOptions CreateExporterOptions(string signalPath)
    {
        var options = new OtlpExporterOptions { Protocol = OtlpExportProtocol.HttpProtobuf };
        options.HttpClientFactory = () => new HttpClient(
            new SocketsHttpHandler { PooledConnectionIdleTimeout = ConnectionIdleTimeout })
        {
            Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds)
        };
        if (BaseEndpoint is not null)
        {
            options.Endpoint = new Uri(BaseEndpoint.AbsoluteUri.TrimEnd('/') + signalPath);
        }

        if (Headers is not null)
        {
            options.Headers = Headers;
        }

        return options;
    }

    /// <summary>
    /// Omits <see cref="Headers"/>, which hold a credential.
    /// </summary>
    public override string ToString()
    {
        return $"{nameof(OtlpExportSettings)} {{ {nameof(BaseEndpoint)} = {BaseEndpoint} }}";
    }
}
