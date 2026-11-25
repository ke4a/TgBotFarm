using System.Diagnostics.Metrics;
using BotFarm.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace BotFarm.Core.UnitTests.Telemetry;

[TestFixture]
public class BotEventMetricsTests
{
    private const string BotName = "TestBot";

    private ServiceProvider _provider = null!;
    private BotEventMetrics _metrics = null!;
    private MetricCollector<long> _events = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = _provider.GetRequiredService<IMeterFactory>();
        _events = new MetricCollector<long>(meterFactory, UpdateMetrics.MeterName, "botfarm.bot.events");
        _metrics = new BotEventMetrics(meterFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _events.Dispose();
        _provider.Dispose();
    }

    [TestCase(BotEvent.BackupCreated, "backup_created")]
    [TestCase(BotEvent.BackupFailed, "backup_failed")]
    [TestCase(BotEvent.BackupRestored, "backup_restored")]
    [TestCase(BotEvent.RestoreFailed, "restore_failed")]
    [TestCase(BotEvent.BotEnabled, "bot_enabled")]
    [TestCase(BotEvent.BotDisabled, "bot_disabled")]
    [TestCase(BotEvent.PendingUpdatesDropped, "pending_updates_dropped")]
    [TestCase(BotEvent.SettingsChanged, "settings_changed")]
    public void Record_AddsOneTaggedWithTheBotAndTheEvent(BotEvent botEvent, string tagValue)
    {
        _metrics.Record(BotName, botEvent);

        var measurement = _events.GetMeasurementSnapshot().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Value, Is.EqualTo(1));
            Assert.That(measurement.Tags, Is.EquivalentTo(new Dictionary<string, object?>
            {
                ["bot"] = BotName,
                ["event"] = tagValue
            }));
        }
    }

    [Test]
    public void Record_UndefinedEvent_IsNotCountedAndDoesNotThrow()
    {
        Assert.DoesNotThrow(() => _metrics.Record(BotName, (BotEvent)42));

        Assert.That(_events.GetMeasurementSnapshot(), Is.Empty);
    }

    [Test]
    public void RecordZeros_AddsZeroForEveryEventOfTheBot()
    {
        _metrics.RecordZeros(BotName);

        var measurements = _events.GetMeasurementSnapshot();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurements.Select(m => m.Value), Is.All.EqualTo(0));
            Assert.That(measurements.Select(m => m.Tags["bot"]), Is.All.EqualTo(BotName));
            Assert.That(measurements.Select(m => m.Tags["event"]), Is.EquivalentTo(new[]
            {
                "backup_created", "backup_failed", "backup_restored", "restore_failed",
                "bot_enabled", "bot_disabled", "pending_updates_dropped", "settings_changed"
            }));
        }
    }
}
