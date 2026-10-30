using BotFarm.Core.Exceptions;
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

    /// <summary>
    /// Runs <paramref name="action"/> while the bot's webhook is temporarily paused for maintenance,
    /// then re-applies the bot's desired state.
    /// </summary>
    /// <exception cref="BotUpdatesPauseException">
    /// The enabled bot's webhook could not be paused; <paramref name="action"/> was not run.
    /// </exception>
    Task<T> RunWithUpdatesPausedAsync<T>(
        string botName,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses the webhooks of all bots ahead of a host shutdown.
    /// </summary>
    Task PauseAllForShutdownAsync(CancellationToken cancellationToken = default);
}
