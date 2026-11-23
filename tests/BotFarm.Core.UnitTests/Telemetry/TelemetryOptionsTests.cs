using BotFarm.Core.Extensions;
using BotFarm.Core.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Telemetry;

[TestFixture]
public class TelemetryOptionsTests
{
    private readonly TelemetryOptionsValidator _validator = new();

    [Test]
    public void Validate_EmptyValues_Succeeds()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_validator.Validate(null, new TelemetryOptions()).Succeeded, Is.True);
            Assert.That(_validator.Validate(null, new TelemetryOptions
            {
                OtlpEndpoint = "",
                OtlpHeaders = "",
                DashboardUrl = " "
            }).Succeeded, Is.True);
        }
    }

    [TestCase("http://localhost:4318")]
    [TestCase("https://otlp-gateway-prod-eu-west-2.grafana.net/otlp")]
    public void Validate_AbsoluteHttpUrls_Succeeds(string url)
    {
        var options = new TelemetryOptions { OtlpEndpoint = url, DashboardUrl = url };

        Assert.That(_validator.Validate(null, options).Succeeded, Is.True);
    }

    [TestCase("localhost:4318")]
    [TestCase("/otlp")]
    [TestCase("ftp://example.com/otlp")]
    [TestCase("not a url")]
    public void Validate_InvalidOtlpEndpoint_FailsNamingTheSetting(string url)
    {
        var result = _validator.Validate(null, new TelemetryOptions { OtlpEndpoint = url });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureMessage, Does.Contain("Telemetry:OtlpEndpoint"));
        }
    }

    [TestCase("localhost:3000/d/botfarm/botfarm")]
    [TestCase("file:///tmp/dashboard")]
    public void Validate_InvalidDashboardUrl_FailsNamingTheSetting(string url)
    {
        var result = _validator.Validate(null, new TelemetryOptions { DashboardUrl = url });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureMessage, Does.Contain("Telemetry:DashboardUrl"));
        }
    }

    [Test]
    public void Validate_Failure_NeverContainsTheHeaders()
    {
        const string headers = "Authorization=Basic c2VjcmV0";
        var result = _validator.Validate(null, new TelemetryOptions { OtlpEndpoint = "bad", OtlpHeaders = headers });

        Assert.That(result.FailureMessage, Does.Not.Contain(headers));
    }

    [Test]
    public async Task Startup_InvalidValue_Fails()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "localhost:4318"
        });

        var validator = provider.GetRequiredService<IAsyncStartupValidator>();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            async () => await validator.ValidateAsync(CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("Telemetry:OtlpEndpoint"));
    }

    [Test]
    public async Task Startup_ValidValues_BindsTheSection()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4318",
            ["Telemetry:OtlpHeaders"] = "Authorization=Basic abc",
            ["Telemetry:DashboardUrl"] = "http://localhost:3000/d/botfarm/botfarm"
        });

        await provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None);
        var options = provider.GetRequiredService<IOptions<TelemetryOptions>>().Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.OtlpEndpoint, Is.EqualTo("http://localhost:4318"));
            Assert.That(options.OtlpHeaders, Is.EqualTo("Authorization=Basic abc"));
            Assert.That(options.DashboardUrl, Is.EqualTo("http://localhost:3000/d/botfarm/botfarm"));
        }
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddCoreServices(configuration, Substitute.For<IHostEnvironment>());
        return services.BuildServiceProvider();
    }
}
