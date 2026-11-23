using System.Diagnostics.Metrics;
using BotFarm.Core.Telemetry;
using BotFarm.Extensions;
using BotFarm.Health;
using BotFarm.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.UnitTests.Telemetry;

[TestFixture]
public class AppMemoryMetricsTests
{
    private ServiceProvider _provider = null!;
    private IMeterFactory _meterFactory = null!;
    private IOptionsMonitor<MemoryCheckOptions> _options = null!;
    private MemoryCheckOptions _memoryCheck = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _meterFactory = _provider.GetRequiredService<IMeterFactory>();
        _memoryCheck = new MemoryCheckOptions { ThresholdMb = 400 };
        _options = Substitute.For<IOptionsMonitor<MemoryCheckOptions>>();
        _options.Get(HealthCheckExtensions.MemoryCheckName).Returns(_ => _memoryCheck);
    }

    [TearDown]
    public void TearDown()
    {
        _provider.Dispose();
    }

    [TestCase("botfarm.memory.working_set")]
    [TestCase("botfarm.memory.gc_heap")]
    public void MemoryGauge_ReportsAPositiveSizeInBytes(string instrumentName)
    {
        using var collector = new MetricCollector<long>(_meterFactory, UpdateMetrics.MeterName, instrumentName);
        _ = new AppMemoryMetrics(_meterFactory, _options);

        collector.RecordObservableInstruments();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collector.Instrument?.Unit, Is.EqualTo("By"));
            Assert.That(collector.LastMeasurement?.Value, Is.Positive);
            Assert.That(collector.LastMeasurement?.Tags, Is.Empty);
        }
    }

    [Test]
    public void ThresholdGauge_ReportsTheMemoryCheckThresholdInBytes()
    {
        using var collector = new MetricCollector<long>(_meterFactory, UpdateMetrics.MeterName,
            "botfarm.memory.threshold");
        _ = new AppMemoryMetrics(_meterFactory, _options);

        collector.RecordObservableInstruments();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collector.Instrument?.Unit, Is.EqualTo("By"));
            Assert.That(collector.LastMeasurement?.Value, Is.EqualTo(400L * 1024 * 1024));
        }
    }

    [Test]
    public void ThresholdGauge_FollowsAChangedThreshold()
    {
        using var collector = new MetricCollector<long>(_meterFactory, UpdateMetrics.MeterName,
            "botfarm.memory.threshold");
        _ = new AppMemoryMetrics(_meterFactory, _options);
        collector.RecordObservableInstruments();

        _memoryCheck = new MemoryCheckOptions { ThresholdMb = 500 };
        collector.RecordObservableInstruments();

        Assert.That(collector.GetMeasurementSnapshot().Select(m => m.Value),
            Is.EqualTo(new[] { 400L * 1024 * 1024, 500L * 1024 * 1024 }));
    }
}
