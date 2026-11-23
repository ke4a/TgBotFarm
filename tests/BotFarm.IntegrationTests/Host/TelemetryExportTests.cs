using System.Collections.Concurrent;
using System.Net;
using System.Text;
using BotFarm.IntegrationTests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http; using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;

namespace BotFarm.IntegrationTests.Host;

/// <summary>
/// Runs the app with metrics export pointed at a fake OTLP endpoint, a Kestrel listener on a free local port, and
/// checks what reaches it: the metrics path, the configured headers and a protobuf body, on a flush and when the app
/// stops. Also checks that export failures are logged once and that no export runs without an endpoint.
/// </summary>
[TestFixture]
public class TelemetryExportTests
{
    private const string HeaderName = "X-Test-Token";
    private const string HeaderValue = "integration-test-token-value";

    /// <summary>The log category of the host's exporter, which logs export outages.</summary>
    private const string ExporterLogCategory = "BotFarm.Telemetry.LoggingMetricExporter";

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

            var request = endpoint.Requests.Last();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(request.Path, Is.EqualTo("/v1/metrics"));
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
            exportsBeforeStop = endpoint.Requests.Count;
        }
        finally
        {
            await app.DisposeAsync();
        }

        var final = endpoint.Requests.Skip(exportsBeforeStop).ToList();
        Assert.That(final, Is.Not.Empty, "Stopping the app sent no final export.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final[^1].Path, Is.EqualTo("/v1/metrics"));
            Assert.That(Contains(final[^1].Body, UpdatesMetricName), Is.True,
                "The final export does not hold the botfarm.updates metric.");
        }
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
                Assert.That(endpoint.Requests, Has.Count.GreaterThanOrEqualTo(3));
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
    public async Task NoEndpoint_RegistersNoMeterProvider()
    {
        await using var app = await BotFarmAppFactory.CreateAsync();

        Assert.That(app.Services.GetService<MeterProvider>(), Is.Null);
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

    /// <summary>
    /// One request the fake endpoint received.
    /// </summary>
    private sealed record ReceivedRequest(
        string Path,
        string? ContentType,
        IReadOnlyDictionary<string, string> Headers,
        byte[] Body);

    /// <summary>
    /// A Kestrel listener on a free local port that records every request and answers with a fixed status code.
    /// </summary>
    private sealed class FakeOtlpEndpoint : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<ReceivedRequest> _requests = new();

        private FakeOtlpEndpoint(WebApplication app)
        {
            _app = app;
        }

        /// <summary>
        /// The base URL the app exports to, without the metrics path.
        /// </summary>
        public string BaseUrl { get; private set; } = string.Empty;

        /// <summary>
        /// The requests received so far, in order.
        /// </summary>
        public IReadOnlyList<ReceivedRequest> Requests => _requests.ToArray();

        public static async Task<FakeOtlpEndpoint> StartAsync(HttpStatusCode status)
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var endpoint = new FakeOtlpEndpoint(app);

            app.Run(async context =>
            {
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body);
                endpoint._requests.Enqueue(new ReceivedRequest(
                    context.Request.Path,
                    context.Request.ContentType,
                    context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(),
                        StringComparer.OrdinalIgnoreCase),
                    body.ToArray()));
                context.Response.StatusCode = (int)status;
            });

            await app.StartAsync();
            endpoint.BaseUrl = app.Services.GetRequiredService<IServer>().Features
                .GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();

            return endpoint;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
