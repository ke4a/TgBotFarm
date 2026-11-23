using System.Diagnostics.Metrics;
using BotFarm.Core.Telemetry;
using BotFarm.Extensions;
using BotFarm.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

namespace BotFarm.UnitTests.Telemetry;

[TestFixture]
public class TelemetryRegistrationTests
{
    // Port 9 (discard) is closed on test machines, so the final export on dispose fails at once.
    private const string AppEndpoint = "http://127.0.0.1:9";
    private const string StandardEndpoint = "http://collector.example.com:4318";

    [Test]
    public void NoEndpoint_RegistersNoMeterProviderNoHostedServiceAndNoMemoryGauges()
    {
        var services = Register(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "",
            ["Telemetry:OtlpHeaders"] = "Authorization=Basic abc"
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.Any(d => d.ServiceType == typeof(MeterProvider)), Is.False);
            Assert.That(services.Any(d => d.ServiceType == typeof(IHostedService)), Is.False);
            Assert.That(services.Any(d => d.ServiceType == typeof(OtlpExportSettings)), Is.False);
            Assert.That(services.Any(d => d.ServiceType == typeof(AppMemoryMetrics)), Is.False);
        }
    }

    [TestCase("http://localhost:4318")]
    [TestCase("http://localhost:4318/")]
    public void AppEndpointAlone_TurnsExportOnWithTheMetricsPathOverHttpProtobuf(string endpoint)
    {
        var services = Register(new Dictionary<string, string?> { ["Telemetry:OtlpEndpoint"] = endpoint });

        var settings = GetSettings(services);
        var options = settings.CreateExporterOptions();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.Any(d => d.ServiceType == typeof(MeterProvider)), Is.True);
            Assert.That(settings.Endpoint, Is.EqualTo(new Uri("http://localhost:4318/v1/metrics")));
            Assert.That(options.Endpoint, Is.EqualTo(new Uri("http://localhost:4318/v1/metrics")));
            Assert.That(options.Protocol, Is.EqualTo(OtlpExportProtocol.HttpProtobuf));
        }
    }

    [Test]
    public void AppEndpointWithABasePath_KeepsThePath()
    {
        var services = Register(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "https://otlp-gateway-prod-eu-west-2.grafana.net/otlp"
        });

        Assert.That(GetSettings(services).Endpoint,
            Is.EqualTo(new Uri("https://otlp-gateway-prod-eu-west-2.grafana.net/otlp/v1/metrics")));
    }

    [Test]
    public void StandardEndpointAlone_TurnsExportOnAndLeavesTheEndpointToTheExporter()
    {
        var services = Register(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = StandardEndpoint });

        var settings = GetSettings(services);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.Any(d => d.ServiceType == typeof(MeterProvider)), Is.True);
            Assert.That(settings.Endpoint, Is.Null);
            Assert.That(settings.CreateExporterOptions().Protocol, Is.EqualTo(OtlpExportProtocol.HttpProtobuf));
        }
    }

    [Test]
    public void ExporterOptions_DropIdleConnectionsBeforeTheNextExport()
    {
        var services = Register(new Dictionary<string, string?> { ["Telemetry:OtlpEndpoint"] = AppEndpoint });

        var options = GetSettings(services).CreateExporterOptions();
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

        Assert.That(GetSettings(services).CreateExporterOptions().Endpoint,
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

        Assert.That(GetSettings(services).CreateExporterOptions().Headers, Is.EqualTo("Authorization=Basic abc"));
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
            Assert.That(settings.CreateExporterOptions().Headers,
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
