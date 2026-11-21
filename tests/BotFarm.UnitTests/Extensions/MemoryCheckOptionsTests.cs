using BotFarm.Extensions;
using BotFarm.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BotFarm.UnitTests.Extensions;

/// <summary>
/// Checks how <see cref="HealthCheckExtensions.ConfigureHealthChecks"/> reads the memory threshold from configuration.
/// </summary>
[TestFixture]
public class MemoryCheckOptionsTests
{
    [Test]
    public void ThresholdMb_WhenNotConfigured_IsTheDefault()
    {
        var options = GetOptions(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.ThresholdMb, Is.EqualTo(MemoryCheckOptions.DefaultThresholdMb));
            Assert.That(options.Threshold, Is.EqualTo(400L * 1024 * 1024));
        }
    }

    [Test]
    public void ThresholdMb_IsReadFromConfiguration()
    {
        var options = GetOptions("1024");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.ThresholdMb, Is.EqualTo(1024));
            Assert.That(options.Threshold, Is.EqualTo(1024L * 1024 * 1024));
        }
    }

    [TestCase("0")]
    [TestCase("-5")]
    public void ThresholdMb_WhenNotPositive_FailsValidationNamingTheSetting(string value)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => GetOptions(value));

        Assert.That(exception!.Message, Does.Contain("HealthChecks:Memory:ThresholdMb"));
    }

    [Test]
    public void ThresholdMb_WhenNotPositive_IsCheckedAtStartup()
    {
        using var provider = BuildProvider("0");

        var validator = provider.GetRequiredService<IAsyncStartupValidator>();

        Assert.ThrowsAsync<OptionsValidationException>(async () => await validator.ValidateAsync(CancellationToken.None));
    }

    private static MemoryCheckOptions GetOptions(string? thresholdMb)
    {
        using var provider = BuildProvider(thresholdMb);
        return provider.GetRequiredService<IOptionsMonitor<MemoryCheckOptions>>()
            .Get(HealthCheckExtensions.MemoryCheckName);
    }

    private static ServiceProvider BuildProvider(string? thresholdMb)
    {
        var values = new Dictionary<string, string?>();
        if (thresholdMb is not null)
        {
            values["HealthChecks:Memory:ThresholdMb"] = thresholdMb;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.ConfigureHealthChecks(configuration);
        return services.BuildServiceProvider();
    }
}
