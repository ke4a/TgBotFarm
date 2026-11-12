using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.Health;

/// <summary>
/// Writes a <see cref="HealthReport"/> as JSON for the <c>/health</c> endpoint.
/// </summary>
/// <remarks>
/// The body holds the overall status and total duration, and for each check its status, description, duration,
/// data, tags and the message of any exception it reported.
/// </remarks>
public static class HealthReportJsonWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Writes <paramref name="report"/> to the response with the <c>application/json</c> content type.
    /// </summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = new
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration,
            Entries = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    Status = entry.Value.Status.ToString(),
                    entry.Value.Description,
                    entry.Value.Duration,
                    entry.Value.Data,
                    entry.Value.Tags,
                    Exception = entry.Value.Exception?.Message
                })
        };

        return JsonSerializer.SerializeAsync(context.Response.Body, body, SerializerOptions, context.RequestAborted);
    }
}
