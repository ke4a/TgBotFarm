using System.Net;
using System.Text;
using BotFarm.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace BotFarm.IntegrationTests.Host;

/// <summary>
/// Runs the app with export pointed at a fake OTLP endpoint, a Kestrel listener on a free local port, and checks what
/// reaches it: metrics on the metrics path and logs on the logs path, with the configured headers and a protobuf
/// body, on a flush and when the app stops. Also checks that metric export failures are logged once and that no
/// export runs without an endpoint.
/// </summary>
[TestFixture]
public class TelemetryExportTests
{
    private const string HeaderName = "X-Test-Token";
    private const string HeaderValue = "integration-test-token-value";

    /// <summary>The log category of the host's exporter, which logs export outages.</summary>
    private const string ExporterLogCategory = "BotFarm.Telemetry.LoggingMetricExporter";

    private const string MetricsPath = "/v1/metrics";
    private const string LogsPath = "/v1/logs";

    private static readonly byte[] UpdatesMetricName = Encoding.UTF8.GetBytes("botfarm.updates");

    [Test]
    public async Task Flush_SendsTheMetricsToTheMetricsPathWithTheConfiguredHeaders()
    {
        await using var endpoint = await FakeOtlpEndpoint.StartAsync(HttpStatusCode.OK);
        var app = await StartAppAsync(endpoint);
        try
        {
            await SendStartAsync(app);

            Assert.That(app.Services.GetRequiredService<MeterProvider>().ForceFlush(), Is.True);

            var request = endpoint.Requests.Last(received => received.Path == MetricsPath);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(request.Headers.GetValueOrDefault(HeaderName), Is.EqualTo(HeaderValue));
                Assert.That(request.ContentType, Is.EqualTo("application/x-protobuf"));
                Assert.That(request.Body, Is.Not.Empty);
                Assert.That(Contains(request.Body, UpdatesMetricName), Is.True,
                    "The export does not hold the botfarm.updates metric.");
            }
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Test]
    public async Task Stop_SendsAFinalExportWithTheLatestUpdate()
    {
        await using var endpoint = await FakeOtlpEndpoint.StartAsync(HttpStatusCode.OK);
        var app = await StartAppAsync(endpoint);
        int exportsBeforeStop;
        try
        {
            await SendStartAsync(app);
            app.Services.GetRequiredService<MeterProvider>().ForceFlush();
            await SendStartAsync(app);
            exportsBeforeStop = endpoint.Requests.Count(received => received.Path == MetricsPath);
        }
        finally
        {
            await app.DisposeAsync();
        }

        var final = endpoint.Requests.Where(received => received.Path == MetricsPath).Skip(exportsBeforeStop).ToList();
        Assert.That(final, Is.Not.Empty, "Stopping the app sent no final export.");
        Assert.That(Contains(final[^1].Body, UpdatesMetricName), Is.True,
            "The final export does not hold the botfarm.updates metric.");
    }

    [Test]
    public async Task RepeatedExportFailures_AreLoggedAsOneWarning()
    {
        await using var endpoint = await FakeOtlpEndpoint.StartAsync(HttpStatusCode.ServiceUnavailable);
        var app = await StartAppAsync(endpoint);
        try
        {
            var meterProvider = app.Services.GetRequiredService<MeterProvider>();
            await SendStartAsync(app);

            meterProvider.ForceFlush();
            meterProvider.ForceFlush();
            meterProvider.ForceFlush();

            var warnings = app.Logs.GetSnapshot()
                .Where(record => record.Category == ExporterLogCategory
                                 && record.Level == LogLevel.Warning)
                .ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(endpoint.Requests.Count(received => received.Path == MetricsPath),
                    Is.GreaterThanOrEqualTo(3));
                Assert.That(warnings, Has.Count.EqualTo(1));
                Assert.That(warnings.Single().Message, Does.Contain("127.0.0.1"));
                Assert.That(warnings.Single().Message, Does.Not.Contain(HeaderValue));
            }
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Test]
    public async Task Flush_SendsTheAppLogsToTheLogsPathWithoutRequestLogs()
    {
        var appLine = $"app-line-{Guid.NewGuid():N}";
        var requestLine = $"request-line-{Guid.NewGuid():N}";
        await using var endpoint = await FakeOtlpEndpoint.StartAsync(HttpStatusCode.OK);
        var app = await StartAppAsync(endpoint);
        try
        {
            var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
            loggerFactory.CreateLogger("BotFarm.LogExportTest").LogInformation("Exported {Line}", appLine);
            loggerFactory.CreateLogger("Microsoft.AspNetCore.LogExportTest").LogInformation("Kept local {Line}",
                requestLine);

            Assert.That(app.Services.GetRequiredService<LoggerProvider>().ForceFlush(), Is.True);

            var logs = endpoint.Requests.Where(received => received.Path == LogsPath).ToList();
            Assert.That(logs, Is.Not.Empty, "No logs reached the logs path.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(logs.All(received => received.Headers.GetValueOrDefault(HeaderName) == HeaderValue),
                    Is.True);
                Assert.That(logs.All(received => received.ContentType == "application/x-protobuf"), Is.True);
                Assert.That(logs.Any(received => Contains(received.Body, Encoding.UTF8.GetBytes(appLine))),
                    Is.True, "The app's information line was not exported.");
                Assert.That(logs.Any(received => Contains(received.Body, Encoding.UTF8.GetBytes(requestLine))),
                    Is.False, "An ASP.NET Core information line was exported.");
            }
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Test]
    public async Task NoEndpoint_RegistersNoMeterProviderAndNoLogExport()
    {
        await using var app = await BotFarmAppFactory.CreateAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.Services.GetService<MeterProvider>(), Is.Null);
            Assert.That(app.Services.GetService<LoggerProvider>(), Is.Null);
        }
    }

    private static Task<BotFarmAppFactory> StartAppAsync(FakeOtlpEndpoint endpoint)
    {
        return BotFarmAppFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = endpoint.BaseUrl,
            ["Telemetry:OtlpHeaders"] = $"{HeaderName}={HeaderValue}"
        });
    }

    private static async Task SendStartAsync(BotFarmAppFactory app)
    {
        var botName = BotFarmAppFactory.BotNames[0];
        var update = TelegramUpdates.Message(TelegramUpdates.PrivateChat(TelegramUpdates.NewChatId()))
            .WithCommand("/start")
            .AsUpdate();

        using var response = await app.SendUpdateAsync(botName, update);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static bool Contains(byte[] body, byte[] value)
    {
        return body.AsSpan().IndexOf(value) >= 0;
    }
}
