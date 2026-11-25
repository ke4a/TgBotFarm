using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Exposes bot control commands, runtime status, and the inbound-processing gate.
/// </summary>
public interface IBotControlService
{
    IReadOnlyList<BotRuntimeStatus> GetStatuses();

    bool CanProcessUpdates(string botName);

    /// <summary>
    /// Stores and applies whether the bot is enabled.
    /// </summary>
    /// <exception cref="BotNotConfiguredException">
    /// The bot is <see cref="BotRuntimeState.NotConfigured"/>; nothing is stored.
    /// </exception>
    /// <exception cref="BotControlBusyException">
    /// Another operation on the bot is in progress or its state is not applied.
    /// </exception>
    Task<BotRuntimeStatus> SetEnabledAsync(
        string botName,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the settings and desired state of a bot that is <see cref="BotRuntimeState.Unknown"/>,
    /// <see cref="BotRuntimeState.Error"/> or <see cref="BotRuntimeState.NotConfigured"/> again and applies them.
    /// </summary>
    /// <exception cref="BotControlBusyException">
    /// Another operation on the bot is in progress or the bot is in another state.
    /// </exception>
    Task<BotRuntimeStatus> RetryAsync(
        string botName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the bot's webhook registration and delivery health from Telegram.
    /// </summary>
    /// <exception cref="BotNotConfiguredException">The bot has no usable settings.</exception>
    Task<WebhookInfo> GetWebhookInfoAsync(
        string botName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the updates Telegram has queued for the bot without changing whether it is enabled.
    /// </summary>
    /// <exception cref="BotControlBusyException">Another operation on the bot is in progress.</exception>
    /// <exception cref="BotNotConfiguredException">The bot has no usable settings.</exception>
    Task DropPendingUpdatesAsync(
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
