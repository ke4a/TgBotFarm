using BotFarm.Core.Models;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Exposes bot control commands, runtime status, and the inbound-processing gate.
/// </summary>
public interface IBotControlService
{
    IReadOnlyList<BotRuntimeStatus> GetStatuses();

    bool CanProcessUpdates(string botName);

    Task<BotRuntimeStatus> SetEnabledAsync(
        string botName,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<BotRuntimeStatus> RetryAsync(
        string botName,
        CancellationToken cancellationToken = default);
}