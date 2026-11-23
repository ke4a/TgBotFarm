using Microsoft.Extensions.Options;

namespace BotFarm.Core.Telemetry;

/// <summary>
/// The <c>Telemetry</c> configuration section: where metrics and logs are exported and where the dashboard that
/// charts the metrics is. Every value is optional.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>
    /// OTLP/HTTP base URL that metrics and logs are exported to, e.g. <c>http://localhost:4318</c>; the host appends
    /// <see cref="Constants.Telemetry.MetricsPath"/> or <see cref="Constants.Telemetry.LogsPath"/>. Takes precedence
    /// over <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>.
    /// Export is off when neither is set.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Export headers in OTLP form, e.g. <c>Authorization=Basic …</c>. Takes precedence over
    /// <c>OTEL_EXPORTER_OTLP_HEADERS</c>. Holds a credential, so it belongs in environment variables or user secrets
    /// only and is never logged.
    /// </summary>
    public string? OtlpHeaders { get; set; }

    /// <summary>
    /// Address of the Grafana dashboard that charts the metrics, used for the admin "Metrics" links. No links are
    /// shown when it is not set.
    /// </summary>
    public string? DashboardUrl { get; set; }
}

/// <summary>
/// Checks <see cref="TelemetryOptions"/> so that a malformed address stops the app at startup with a message naming
/// the setting. Messages never contain <see cref="TelemetryOptions.OtlpHeaders"/>.
/// </summary>
internal sealed class TelemetryOptionsValidator : IValidateOptions<TelemetryOptions>
{
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options)
    {
        var failures = new List<string>();

        AddFailureIfInvalid(failures, nameof(TelemetryOptions.OtlpEndpoint), options.OtlpEndpoint);
        AddFailureIfInvalid(failures, nameof(TelemetryOptions.DashboardUrl), options.DashboardUrl);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddFailureIfInvalid(List<string> failures, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsAbsoluteHttpUrl(value))
        {
            return;
        }

        failures.Add($"{Constants.Telemetry.SectionName}:{key} must be an absolute http or https URL.");
    }

    private static bool IsAbsoluteHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
