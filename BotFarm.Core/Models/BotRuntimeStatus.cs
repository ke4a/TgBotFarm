namespace BotFarm.Core.Models;

/// <summary>
/// Process-local status of applying a bot's durable desired state.
/// </summary>
public sealed record BotRuntimeStatus(
    string BotName,
    bool? DesiredEnabled,
    bool? AppliedEnabled,
    BotRuntimeState Status,
    DateTime? LastAttemptAtUtc,
    string? LastError);

/// <summary>
/// Runtime application state for a bot control command.
/// </summary>
public enum BotRuntimeState
{
    Unknown,
    Pending,
    Applied,
    Error
}