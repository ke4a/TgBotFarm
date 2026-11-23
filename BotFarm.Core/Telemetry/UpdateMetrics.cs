using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BotFarm.Core.Telemetry;

/// <summary>
/// Records how each bot's webhook deliveries are handled: throughput by update kind, handling time, outcomes and
/// command usage, on the meter <see cref="MeterName"/>. Measurements carry only the bot's name, the update kind, the
/// outcome and the command key as tags.
/// </summary>
/// <remarks>
/// Every method does no I/O, never throws and is safe to call concurrently. When nothing listens to the meter, the
/// calls return almost at once.
/// </remarks>
public sealed class UpdateMetrics
{
    /// <summary>Name of the meter that holds BotFarm's instruments.</summary>
    public const string MeterName = "BotFarm";

    private const string BotTag = "bot";
    private const string KindTag = "kind";
    private const string OutcomeTag = "outcome";
    private const string CommandTag = "command";

    private static readonly double[] DurationBoundaries = [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60];
    private static readonly UpdateKind[] Kinds = Enum.GetValues<UpdateKind>();
    private static readonly UpdateOutcome[] Outcomes = Enum.GetValues<UpdateOutcome>();

    private readonly Counter<long> _updates;
    private readonly Counter<long> _outcomes;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _commands;

    /// <summary>
    /// Creates the meter and its instruments through <paramref name="meterFactory"/>.
    /// </summary>
    public UpdateMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _updates = meter.CreateCounter<long>(
            "botfarm.updates",
            "{update}",
            "Webhook deliveries whose update was processed.");
        _outcomes = meter.CreateCounter<long>(
            "botfarm.update.outcomes",
            "{delivery}",
            "Webhook deliveries that failed, were retried, dropped, skipped as duplicates or rejected.");
        _duration = meter.CreateHistogram(
            "botfarm.update.duration",
            "s",
            "Time from just after the duplicate check until processing ended.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBoundaries });
        _commands = meter.CreateCounter<long>(
            "botfarm.commands",
            "{call}",
            "Processed commands, by command key.");
    }

    /// <summary>
    /// Records one processed delivery of the bot named <paramref name="botName"/>: its kind, its handling time and,
    /// for a command, its key (<see cref="Constants.Telemetry.UnknownCommand"/> when it has none).
    /// </summary>
    public void RecordProcessed(string botName, UpdateDescription description, TimeSpan duration)
    {
        try
        {
            _updates.Add(1, new TagList
            {
                { BotTag, botName },
                { KindTag, description.Kind.ToTagValue() }
            });
            _duration.Record(duration.TotalSeconds, new TagList { { BotTag, botName } });

            if (description.Kind == UpdateKind.Command)
            {
                _commands.Add(1, new TagList
                {
                    { BotTag, botName },
                    { CommandTag, description.CommandKey ?? Constants.Telemetry.UnknownCommand }
                });
            }
        }
        catch (Exception)
        {
            // measuring must never affect update handling
        }
    }

    /// <summary>
    /// Records one delivery of the bot named <paramref name="botName"/> that ended with <paramref name="outcome"/>.
    /// </summary>
    public void RecordOutcome(string botName, UpdateOutcome outcome)
    {
        try
        {
            _outcomes.Add(1, new TagList
            {
                { BotTag, botName },
                { OutcomeTag, outcome.ToTagValue() }
            });
        }
        catch (Exception)
        {
            // measuring must never affect update handling
        }
    }

    /// <summary>
    /// Adds zero for every update kind, every outcome, every key in <paramref name="commandKeys"/> and
    /// <see cref="Constants.Telemetry.UnknownCommand"/> of the bot named <paramref name="botName"/>, so each of its
    /// series exists from the start.
    /// </summary>
    public void RecordZeros(string botName, IReadOnlyCollection<string> commandKeys)
    {
        try
        {
            foreach (var kind in Kinds)
            {
                _updates.Add(0, new TagList
                {
                    { BotTag, botName },
                    { KindTag, kind.ToTagValue() }
                });
            }

            foreach (var outcome in Outcomes)
            {
                _outcomes.Add(0, new TagList
                {
                    { BotTag, botName },
                    { OutcomeTag, outcome.ToTagValue() }
                });
            }

            foreach (var key in commandKeys.Append(Constants.Telemetry.UnknownCommand).Distinct(StringComparer.Ordinal))
            {
                _commands.Add(0, new TagList
                {
                    { BotTag, botName },
                    { CommandTag, key }
                });
            }
        }
        catch (Exception)
        {
            // measuring must never affect the caller
        }
    }
}
