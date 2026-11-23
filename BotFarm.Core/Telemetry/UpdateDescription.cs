namespace BotFarm.Core.Telemetry;

/// <summary>
/// How an update is counted in a bot's throughput.
/// </summary>
public enum UpdateKind
{
    /// <summary>A message that starts with a bot command meant for this bot. Tag value <c>command</c>.</summary>
    Command,

    /// <summary>A callback query, sent when a user presses an inline button. Tag value <c>callback_query</c>.</summary>
    CallbackQuery,

    /// <summary>Any other update. Tag value <c>other</c>.</summary>
    Other
}

/// <summary>
/// What a delivery of an update to a bot's webhook ended with, beyond being processed.
/// </summary>
public enum UpdateOutcome
{
    /// <summary>Processing ended with an error, temporary or permanent. Tag value <c>failed</c>.</summary>
    Failed,

    /// <summary>The delivery was answered with a request to deliver it again. Tag value <c>retried</c>.</summary>
    Retried,

    /// <summary>The update was given up after its last allowed temporary failure. Tag value <c>dropped</c>.</summary>
    Dropped,

    /// <summary>The update had already been accepted and was acknowledged without processing. Tag value <c>duplicate</c>.</summary>
    Duplicate,

    /// <summary>The request was answered as having an invalid secret or as the bot not being ready. Tag value <c>rejected</c>.</summary>
    Rejected
}

/// <summary>
/// How an update is counted in a bot's metrics.
/// </summary>
/// <param name="Kind">The update's kind.</param>
/// <param name="CommandKey">
/// For a command, its key in lower case without <c>/</c> and <c>@botname</c>, or
/// <see cref="Constants.Telemetry.UnknownCommand"/> for a command that is neither built in nor registered; otherwise
/// null.
/// </param>
public readonly record struct UpdateDescription(UpdateKind Kind, string? CommandKey);

/// <summary>
/// The metric tag values of <see cref="UpdateKind"/> and <see cref="UpdateOutcome"/>.
/// </summary>
public static class UpdateTagValues
{
    /// <summary>
    /// Returns the value of the <c>kind</c> tag for <paramref name="kind"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
    public static string ToTagValue(this UpdateKind kind) => kind switch
    {
        UpdateKind.Command => "command",
        UpdateKind.CallbackQuery => "callback_query",
        UpdateKind.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>
    /// Returns the value of the <c>outcome</c> tag for <paramref name="outcome"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="outcome"/> is not a defined value.</exception>
    public static string ToTagValue(this UpdateOutcome outcome) => outcome switch
    {
        UpdateOutcome.Failed => "failed",
        UpdateOutcome.Retried => "retried",
        UpdateOutcome.Dropped => "dropped",
        UpdateOutcome.Duplicate => "duplicate",
        UpdateOutcome.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
    };
}
