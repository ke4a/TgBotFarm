using System.Diagnostics.Metrics;
using BotFarm.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace BotFarm.Core.UnitTests.Telemetry;

[TestFixture]
public class UpdateMetricsTests
{
    private const string BotName = "TestBot";

    private static readonly string[] AllowedTags = ["bot", "kind", "outcome", "command"];

    private ServiceProvider _provider = null!;
    private IMeterFactory _meterFactory = null!;
    private UpdateMetrics _metrics = null!;
    private MetricCollector<long> _updates = null!;
    private MetricCollector<long> _outcomes = null!;
    private MetricCollector<double> _duration = null!;
    private MetricCollector<long> _commands = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _meterFactory = _provider.GetRequiredService<IMeterFactory>();
        _updates = new MetricCollector<long>(_meterFactory, UpdateMetrics.MeterName, "botfarm.updates");
        _outcomes = new MetricCollector<long>(_meterFactory, UpdateMetrics.MeterName, "botfarm.update.outcomes");
        _duration = new MetricCollector<double>(_meterFactory, UpdateMetrics.MeterName, "botfarm.update.duration");
        _commands = new MetricCollector<long>(_meterFactory, UpdateMetrics.MeterName, "botfarm.commands");
        _metrics = new UpdateMetrics(_meterFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _updates.Dispose();
        _outcomes.Dispose();
        _duration.Dispose();
        _commands.Dispose();
        _provider.Dispose();
    }

    [Test]
    public void RecordProcessed_Command_CountsTheUpdateItsDurationAndTheCommand()
    {
        _metrics.RecordProcessed(BotName, new UpdateDescription(UpdateKind.Command, "help"),
            TimeSpan.FromMilliseconds(250));

        var update = _updates.GetMeasurementSnapshot().Single();
        var duration = _duration.GetMeasurementSnapshot().Single();
        var command = _commands.GetMeasurementSnapshot().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(update.Value, Is.EqualTo(1));
            Assert.That(update.Tags, Is.EquivalentTo(Tags(("bot", BotName), ("kind", "command"))));
            Assert.That(duration.Value, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(duration.Tags, Is.EquivalentTo(Tags(("bot", BotName))));
            Assert.That(command.Value, Is.EqualTo(1));
            Assert.That(command.Tags, Is.EquivalentTo(Tags(("bot", BotName), ("command", "help"))));
        }
    }

    [TestCase(UpdateKind.CallbackQuery, "callback_query")]
    [TestCase(UpdateKind.Other, "other")]
    public void RecordProcessed_NotACommand_CountsTheUpdateAndDurationButNoCommand(UpdateKind kind, string kindTag)
    {
        _metrics.RecordProcessed(BotName, new UpdateDescription(kind, null), TimeSpan.FromSeconds(2));

        var update = _updates.GetMeasurementSnapshot().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(update.Value, Is.EqualTo(1));
            Assert.That(update.Tags, Is.EquivalentTo(Tags(("bot", BotName), ("kind", kindTag))));
            Assert.That(_duration.GetMeasurementSnapshot().Single().Value, Is.EqualTo(2));
            Assert.That(_commands.GetMeasurementSnapshot(), Is.Empty);
        }
    }

    [TestCase(UpdateOutcome.Failed, "failed")]
    [TestCase(UpdateOutcome.Retried, "retried")]
    [TestCase(UpdateOutcome.Dropped, "dropped")]
    [TestCase(UpdateOutcome.Duplicate, "duplicate")]
    [TestCase(UpdateOutcome.Rejected, "rejected")]
    public void RecordOutcome_TagsTheOutcomeWithItsLowerCaseValue(UpdateOutcome outcome, string outcomeTag)
    {
        _metrics.RecordOutcome(BotName, outcome);

        var measurement = _outcomes.GetMeasurementSnapshot().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Value, Is.EqualTo(1));
            Assert.That(measurement.Tags, Is.EquivalentTo(Tags(("bot", BotName), ("outcome", outcomeTag))));
            Assert.That(_updates.GetMeasurementSnapshot(), Is.Empty);
        }
    }

    [Test]
    public void UndefinedKindOrOutcome_IsNotCountedAndDoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            _metrics.RecordProcessed(BotName, new UpdateDescription((UpdateKind)42, null), TimeSpan.FromSeconds(1));
            _metrics.RecordOutcome(BotName, (UpdateOutcome)42);
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updates.GetMeasurementSnapshot(), Is.Empty);
            Assert.That(_outcomes.GetMeasurementSnapshot(), Is.Empty);
        }
    }

    [Test]
    public void RecordZeros_AddsZeroForEveryKindOutcomeAndCommandKey()
    {
        _metrics.RecordZeros(BotName, ["start", "changelanguage", "getlastgif"]);

        var updates = _updates.GetMeasurementSnapshot();
        var outcomes = _outcomes.GetMeasurementSnapshot();
        var commands = _commands.GetMeasurementSnapshot();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updates.Select(m => m.Value).Concat(outcomes.Select(m => m.Value))
                    .Concat(commands.Select(m => m.Value)), Is.All.Zero);
            Assert.That(updates.Select(m => m.Tags["kind"]),
                Is.EquivalentTo(new[] { "command", "callback_query", "other" }));
            Assert.That(outcomes.Select(m => m.Tags["outcome"]),
                Is.EquivalentTo(new[] { "failed", "retried", "dropped", "duplicate", "rejected" }));
            Assert.That(commands.Select(m => m.Tags["command"]),
                Is.EquivalentTo(new[] { "start", "changelanguage", "getlastgif", Constants.Telemetry.UnknownCommand }));
            Assert.That(updates.Concat(outcomes).Concat(commands).Select(m => m.Tags["bot"]), Is.All.EqualTo(BotName));
            Assert.That(_duration.GetMeasurementSnapshot(), Is.Empty);
        }
    }

    [Test]
    public void DurationHistogram_AdvertisesTheHandlingTimeBoundaries()
    {
        var histogram = (Histogram<double>)_duration.Instrument!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(histogram.Unit, Is.EqualTo("s"));
            Assert.That(histogram.Advice?.HistogramBucketBoundaries,
                Is.EqualTo(new[] { 0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60 }));
        }
    }

    [Test]
    public void Instruments_HaveTheDocumentedUnits()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_updates.Instrument!.Unit, Is.EqualTo("{update}"));
            Assert.That(_outcomes.Instrument!.Unit, Is.EqualTo("{delivery}"));
            Assert.That(_commands.Instrument!.Unit, Is.EqualTo("{call}"));
        }
    }

    [Test]
    public void Measurements_NeverCarryOtherTags()
    {
        _metrics.RecordZeros(BotName, ["start"]);
        _metrics.RecordProcessed(BotName, new UpdateDescription(UpdateKind.Command, null), TimeSpan.Zero);
        _metrics.RecordProcessed(BotName, new UpdateDescription(UpdateKind.Other, null), TimeSpan.Zero);
        _metrics.RecordOutcome(BotName, UpdateOutcome.Failed);

        var tagNames = _updates.GetMeasurementSnapshot().SelectMany(m => m.Tags.Keys)
            .Concat(_outcomes.GetMeasurementSnapshot().SelectMany(m => m.Tags.Keys))
            .Concat(_duration.GetMeasurementSnapshot().SelectMany(m => m.Tags.Keys))
            .Concat(_commands.GetMeasurementSnapshot().SelectMany(m => m.Tags.Keys))
            .Distinct();

        Assert.That(tagNames, Is.SubsetOf(AllowedTags));
    }

    [Test]
    public void RecordProcessed_CommandWithoutAKey_CountsTheUnknownKey()
    {
        _metrics.RecordProcessed(BotName, new UpdateDescription(UpdateKind.Command, null), TimeSpan.Zero);

        Assert.That(_commands.GetMeasurementSnapshot().Single().Tags["command"],
            Is.EqualTo(Constants.Telemetry.UnknownCommand));
    }

    private static Dictionary<string, object?> Tags(params (string Key, object? Value)[] tags)
    {
        return tags.ToDictionary(tag => tag.Key, tag => tag.Value);
    }
}
