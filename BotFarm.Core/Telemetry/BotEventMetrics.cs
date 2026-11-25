using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BotFarm.Core.Telemetry;

/// <summary>
/// An operator-visible event in a bot's life, counted so the monitoring dashboard can mark when it happened.
/// </summary>
public enum BotEvent
{
    /// <summary>A backup archive of the bot's database was created.</summary>
    BackupCreated,

    /// <summary>A backup of the bot's database was attempted and failed.</summary>
    BackupFailed,

    /// <summary>The bot's database was restored from a backup archive.</summary>
    BackupRestored,

    /// <summary>A restore of the bot's database was attempted and failed.</summary>
    RestoreFailed,

    /// <summary>An operator turned the bot on, and its webhook was set.</summary>
    BotEnabled,

    /// <summary>An operator turned the bot off, and its webhook was removed.</summary>
    BotDisabled,

    /// <summary>An operator discarded the updates Telegram had queued for the bot.</summary>
    PendingUpdatesDropped,

    /// <summary>An administrator saved a change to the bot's settings.</summary>
    SettingsChanged
}

/// <summary>
/// Counts each bot's <see cref="BotEvent"/>s on the meter <see cref="UpdateMetrics.MeterName"/>. Measurements carry
/// only the bot's name and the event as tags.
/// </summary>
/// <remarks>
/// Every method does no I/O, never throws and is safe to call concurrently. When nothing listens to the meter, the
/// calls return almost at once.
/// </remarks>
public sealed class BotEventMetrics
{
    private const string BotTag = "bot";
    private const string EventTag = "event";

    private static readonly BotEvent[] Events = Enum.GetValues<BotEvent>();

    private readonly Counter<long> _events;

    /// <summary>
    /// Creates the counter through <paramref name="meterFactory"/>.
    /// </summary>
    public BotEventMetrics(IMeterFactory meterFactory)
    {
        _events = meterFactory.Create(UpdateMetrics.MeterName).CreateCounter<long>(
            "botfarm.bot.events",
            "{event}",
            "Backups, restores, on/off changes, dropped pending updates and settings changes of each bot.");
    }

    /// <summary>
    /// Records that <paramref name="botEvent"/> happened to the bot named <paramref name="botName"/>.
    /// </summary>
    public void Record(string botName, BotEvent botEvent)
    {
        Add(botName, botEvent, 1);
    }

    /// <summary>
    /// Adds zero for every <see cref="BotEvent"/> of the bot named <paramref name="botName"/>, so each of its series
    /// exists from the start and the first event after startup is counted.
    /// </summary>
    public void RecordZeros(string botName)
    {
        foreach (var botEvent in Events)
        {
            Add(botName, botEvent, 0);
        }
    }

    private void Add(string botName, BotEvent botEvent, long value)
    {
        try
        {
            _events.Add(value, new TagList
            {
                { BotTag, botName },
                { EventTag, botEvent.ToTagValue() }
            });
        }
        catch (Exception)
        {
            // measuring must never affect the caller
        }
    }
}

/// <summary>
/// Converts <see cref="BotEvent"/> values to the tag values reported to the monitoring service.
/// </summary>
public static class BotEventTagValues
{
    /// <summary>
    /// Returns the value of the <c>event</c> tag for <paramref name="botEvent"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="botEvent"/> is not a defined value.</exception>
    public static string ToTagValue(this BotEvent botEvent) => botEvent switch
    {
        BotEvent.BackupCreated => "backup_created",
        BotEvent.BackupFailed => "backup_failed",
        BotEvent.BackupRestored => "backup_restored",
        BotEvent.RestoreFailed => "restore_failed",
        BotEvent.BotEnabled => "bot_enabled",
        BotEvent.BotDisabled => "bot_disabled",
        BotEvent.PendingUpdatesDropped => "pending_updates_dropped",
        BotEvent.SettingsChanged => "settings_changed",
        _ => throw new ArgumentOutOfRangeException(nameof(botEvent), botEvent, null)
    };
}
