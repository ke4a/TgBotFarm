using BotFarm.Core.Models;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Persists authoritative desired bot states.
/// </summary>
public interface IBotControlStateStore
{
    Task<BotControlState?> GetAsync(string botName, CancellationToken cancellationToken = default);

    Task<bool> TryInsertAsync(BotControlState state, CancellationToken cancellationToken = default);

    Task<BotControlState?> TrySetDesiredStateAsync(
        string botName,
        bool desiredEnabled,
        string commandId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);
}