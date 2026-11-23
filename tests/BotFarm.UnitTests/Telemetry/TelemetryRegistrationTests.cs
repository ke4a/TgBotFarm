using System.Diagnostics.Metrics;
using BotFarm.Core;
using BotFarm.Core.Telemetry;
using BotFarm.Extensions;
using BotFarm.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace BotFarm.UnitTests.Telemetry;

[TestFixture]
public class TelemetryRegistrationTests
{
    // Port 9 (discard) is closed on test machines, so the final export on dispose fails at once.
    private const string AppEndpoint = "http://127.0.0.1:9";
    private const string StandardEndpoint = "http://collector.example.com:4318";

    [Test]
    public void NoEndpoint_RegistersNoMeterProviderNoLogExportNoHostedServiceAndNoMemoryGauges()
    {
        var services = Register(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "",
            ["Telemetry:OtlpHeaders"] = "Authorization=Basic abc"
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.Any(d => d.ServiceType == typeof(MeterProvider)), Is.False);
            Assert.That(services.Any(d => d.ServiceType == typeof(LoggerProvider)), Is.False);
            Assert.That(services.Any(d => d.ServiceType == typeof(IHostedService)), Is.False);
            Assert.That(services.Any(d => d.ServiceType == typeof(OtlpExportSettings)), Is.False);
            Assert.That(services.Any(d => d.ServiceType == typeof(AppMemoryMetrics)), Is.False);
        }
    }

    [TestCase("http://localhost:4318")]
    [TestCase("http://localhost:4318/")]
    public void AppEndpointAlone_TurnsExportOnWithTheSignalPathsOverHttpProtobuf(string endpoint)
    {
        var services = Register(new Dictionary<string, string?> { ["Telemetry:OtlpEndpoint"] = endpoint });

        var settings = GetSettings(services);
        var metrics = settings.CreateExporterOptions(Constants.Telemetry.MetricsPath);
        var logs = settings.CreateExporterOptions(Constants.Telemetry.LogsPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.Any(d => d.ServiceType == typeof(MeterProvider)), Is.True);
            Assert.That(services.Any(d => d.ServiceType == typeof(LoggerProvider)), Is.True);
            Assert.That(settings.BaseEndpoint, Is.EqualTo(new Uri("http://localhost:4318")));
            Assert.That(metrics.Endpoint, Is.EqualTo(new Uri("http://localhost:4318/v1/metrics")));
            Assert.That(logs.Endpoint, Is.EqualTo(new Uri("http://localhost:4318/v1/logs")));
            Assert.That(metrics.Protocol, Is.EqualTo(OtlpExportProtocol.HttpProtobuf));
            Assert.That(logs.Protocol, Is.EqualTo(OtlpExportProtocol.HttpProtobuf));
        }
    }

    [Test]
    public void AppEndpointWithABasePath_KeepsThePath()
    {
        var services = Register(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "https://otlp-gateway-prod-eu-west-2.grafana.net/otlp"
        });

        var settings = GetSettings(services);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.CreateExporterOptions(Constants.Telemetry.MetricsPath).Endpoint,
                Is.EqualTo(new Uri("https://otlp-gateway-prod-eu-west-2.grafana.net/otlp/v1/metrics")));
            Assert.That(settings.CreateExporterOptions(Constants.Telemetry.LogsPath).Endpoint,
                Is.EqualTo(new Uri("https://otlp-gateway-prod-eu-west-2.grafana.net/otlp/v1/logs")));
        }
    }

    [Test]
    public void StandardEndpointAlone_TurnsExportOnAndLeavesTheEndpointToTheExporter()
    {
        var services = Register(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = StandardEndpoint });

        var settings = GetSettings(services);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.Any(d => d.ServiceType == typeof(MeterProvider)), Is.True);
            Assert.That(services.Any(d => d.ServiceType == typeof(LoggerProvider)), Is.True);
            Assert.That(settings.BaseEndpoint, Is.Null);
            Assert.That(settings.CreateExporterOptions(Constants.Telemetry.LogsPath).Protocol, Is.EqualTo(OtlpExportProtocol.HttpProtobuf));
        }
    }

    [Test]
    public void ExporterOptions_DropIdleConnectionsBeforeTheNextExport()
    {
        var services = Register(new Dictionary<string, string?> { ["Telemetry:OtlpEndpoint"] = AppEndpoint });

        var options = GetSettings(services).CreateExporterOptions(Constants.Telemetry.MetricsPath);
        using var client = options.HttpClientFactory();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(OtlpExportSettings.ConnectionIdleTimeout, Is.LessThan(TelemetryExtensions.ExportInterval));
            Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromMilliseconds(options.TimeoutMilliseconds)));
        }
    }

    [Test]
    public void BothEndpoints_AppEndpointWins()
    {
        var services = Register(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4318",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = StandardEndpoint
        });

        Assert.That(GetSettings(services).CreateExporterOptions(Constants.Telemetry.MetricsPath).Endpoint,
            Is.EqualTo(new Uri("http://localhost:4318/v1/metrics")));
    }

    [Test]
    public void AppHeaders_SetTheExporterHeaders()
    {
        var services = Register(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4318",
            ["Telemetry:OtlpHeaders"] = "Authorization=Basic abc"
        });

        Assert.That(GetSettings(services).CreateExporterOptions(Constants.Telemetry.MetricsPath).Headers, Is.EqualTo("Authorization=Basic abc"));
    }

    [Test]
    public void NoAppHeaders_LeavesTheHeadersToTheStandardVariable()
    {
        var services = Register(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4318",
            ["Telemetry:OtlpHeaders"] = ""
        });

        var settings = GetSettings(services);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(settings.Headers, Is.Null);
            Assert.That(settings.CreateExporterOptions(Constants.Telemetry.MetricsPath).Headers,
                Is.EqualTo(new OtlpExporterOptions().Headers));
        }
    }

    [Test]
    public void ExportOn_ListensToTheBotFarmAndRuntimeMetersOnly()
    {
        var services = Register(new Dictionary<string, string?> { ["Telemetry:OtlpEndpoint"] = AppEndpoint });
        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<MeterProvider>();

        using var botFarm = new Meter(UpdateMetrics.MeterName);
        using var runtime = new Meter("System.Runtime");
        using var aspNetCore = new Meter("Microsoft.AspNetCore.Hosting");
        using var httpClient = new Meter("System.Net.Http");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(botFarm.CreateCounter<long>("test.botfarm").Enabled, Is.True);
            Assert.That(runtime.CreateCounter<long>("test.runtime").Enabled, Is.True);
            Assert.That(aspNetCore.CreateCounter<long>("test.aspnetcore").Enabled, Is.False);
            Assert.That(httpClient.CreateCounter<long>("test.http").Enabled, Is.False);
        }
    }

    [Test]
    public void ExportOn_AddsTheOpenTelemetryLoggerProviderWithFormattedMessages()
    {
        var services = Register(new Dictionary<string, string?> { ["Telemetry:OtlpEndpoint"] = AppEndpoint });
        using var provider = services.BuildServiceProvider();

        var loggerProviders = provider.GetServices<ILoggerProvider>();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenTelemetryLoggerOptions>>()
            .Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loggerProviders.OfType<OpenTelemetryLoggerProvider>(), Has.One.Items);
            Assert.That(options.IncludeFormattedMessage, Is.True);
        }
    }

    [Test]
    public void ExportOn_RegistersTheMemoryGaugesAsASingleton()
    {
        var services = Register(new Dictionary<string, string?> { ["Telemetry:OtlpEndpoint"] = AppEndpoint });

        Assert.That(services.Single(d => d.ServiceType == typeof(AppMemoryMetrics)).Lifetime,
            Is.EqualTo(ServiceLifetime.Singleton));
    }

    private static IServiceCollection Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddBotFarmTelemetry(configuration);
        return services;
    }

    private static OtlpExportSettings GetSettings(IServiceCollection services)
    {
        return (OtlpExportSettings)services.Single(d => d.ServiceType == typeof(OtlpExportSettings))
            .ImplementationInstance!;
    }
}
