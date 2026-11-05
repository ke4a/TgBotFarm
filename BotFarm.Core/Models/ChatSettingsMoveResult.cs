namespace BotFarm.Core.Models;

/// <summary>
/// Outcome of moving a chat's settings document from one chat ID to another.
/// </summary>
internal enum ChatSettingsMoveResult
{
    /// <summary>No settings were stored under the source chat ID.</summary>
    NotFound,

    /// <summary>The settings were copied to the target chat ID and removed from the source chat ID.</summary>
    Moved,

    /// <summary>
    /// Settings already existed under the target chat ID and were kept; the source settings were removed.
    /// </summary>
    KeptExisting
}
